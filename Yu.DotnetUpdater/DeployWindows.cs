using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Web.Administration;
using System.Security.Cryptography.X509Certificates;

namespace Yu.DotnetUpdater
{
    /// <summary>
    /// 部署至Windows服务/IIS
    /// </summary>
    /// <remarks>
    /// 更新配置文件-->检测更新包-->备份项目原文件-->根据项目类型执行更新：<br/>
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("linux")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("tvos")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("ios")]
    internal partial class DeployWindows : Util
    {
        #region Start
        /// <summary>
        /// Start
        /// </summary>
        /// <param name="services">待更新项</param>
        internal static void Start(UpdateServiceConf[] services)
        {
            var stopwatch = new Stopwatch();
            var deployPath = Configuration["DeployPath"];
            for (var i = 0; i < services.Length; i++)
            {
                stopwatch.Reset();
                stopwatch.Start();
                if (i > 0) Info(string.Empty);

                #region 查找待更新文件
                Info($"{services[i].UpdatePack}->查找待更新文件...");
                string zipFile = Path.Combine(ToolRunPath, services[i].UpdatePack);
                if (!File.Exists(zipFile))
                {
                    Info($"{services[i].UpdatePack}->未找到待更新压缩包 : {zipFile}");
                    continue;
                }
                #endregion
                string updatePath = Path.Combine(deployPath, services[i].Path);
                CreateFolder(updatePath);
                #region 备份原文件
                if (UpdateServiceConf.BakDirectoryNameDemo.Contains(services[i].BakDirectoryFormat))
                {
                    Info($"{services[i].ServiceName}->备份原文件...");
                    string bakPathBase = Path.Combine(deployPath, "bak");
                    string pathBak = Path.Combine(bakPathBase, DateTime.Now.ToString(services[i].BakDirectoryFormat));
                    CopyFolderFile(updatePath, pathBak, true, "logs", "log");

                    Info($"{services[i].ServiceName}->....................................");
                    DeleteOldBak(bakPathBase);
                    Info($"{services[i].ServiceName}->备份原文件完成.");
                }
                #endregion
                var updateMode = (UpdateMode)services[i].UpdateMode;
                Info($"更新模式：{updateMode}");
                if (services[i].IISConf != null && !string.IsNullOrWhiteSpace(services[i].IISConf.SiteName))
                {
                    IISSiteUpdate(services[i], zipFile, updatePath, updateMode);
                }
                else
                {
                    UpdateService(updateMode, services[i], zipFile, deployPath, updatePath);
                }
                stopwatch.Stop();
                WriteGreen($"[{services[i].UpdatePack}]更新耗时:{stopwatch.ElapsedMilliseconds}ms");
                Thread.Sleep(2000);
            }
        }
        #endregion

        #region IIS站点更新
        /// <summary>
        /// IIS站点更新
        /// </summary>
        /// <remarks>
        /// 方式一：停止应用程序池-->停止站点-->更新文件-->启动应用程序池-->启动站点<br/>
        /// 方式二：原文件重命名-->更新文件-->回收应用程序池-->数秒后清理重命名的文件<br/>
        /// </remarks>
        private static void IISSiteUpdate(UpdateServiceConf service, string zipFile, string updatePath, UpdateMode mode)
        {
            var stopwatch = new Stopwatch();
            try
            {
                if (service.Ports == null || !service.Ports.Any(p => p > 0))
                {
                    WriteRed($"{service.ServiceName}->缺少端口");
                    return;
                }
                stopwatch.Start();
                if (!CreateIISSite(service.IISConf, updatePath, service.Ports[0])) { return; }
                if (mode != UpdateMode.Cold)
                {
                    RenameTargetFile(zipFile, updatePath, service.ServiceName);
                    Info($"{service.UpdatePack}->解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    ApppoolStatusUpdate(service.IISConf.AppPool.AppPoolName, 0);
                    new Thread(delegate () { DelTmpFile(updatePath, 10, false); }) { IsBackground = true }.Start();
                }
                else
                {
                    ApppoolStatusUpdate(service.IISConf.AppPool.AppPoolName, 2);
                    StartStopSite(service.IISConf.SiteName, false);
                    Thread.Sleep(2000);
                    Info($"{service.UpdatePack}->解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    ApppoolStatusUpdate(service.IISConf.AppPool.AppPoolName, 1);
                    StartStopSite(service.IISConf.SiteName, true);
                }
                if (!string.IsNullOrWhiteSpace(service.IISConf.OpenUrl))
                {
                    Thread.Sleep(2000);
                    Task.Run(async () =>
                    {
                        Info($"{service.UpdatePack}->请求一次：{service.IISConf.OpenUrl}");
                        try
                        {
                            using var client = new HttpClient();
                            var resp = await client.PostAsync(service.IISConf.OpenUrl, default);
                            await resp.Content.ReadAsStringAsync();
                            //client.PostAsync(service.OpenUrl, default).ContinueWith(res => res.Result.Content.ReadAsStringAsync().Wait(10000));
                        }
                        catch (Exception ex)
                        {
                            WriteRed($"请求url出错[{service.IISConf.OpenUrl}]{ex.Message}]", ex);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                WriteRed($"{service.UpdatePack}->[{nameof(IISSiteUpdate)}]", ex);
            }
            finally
            {
                stopwatch.Stop();
                WriteGreen($"[{service.UpdatePack}]耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        #endregion
        #region 更新IIS应用程序池状态
        /// <summary>
        /// 更新IIS应用程序池状态
        /// </summary>
        /// <param name="appPoolName">程序池名</param>
        /// <param name="type">操作类型 0-回收，1-启动，2-停止</param>
        private static void ApppoolStatusUpdate(string appPoolName, int type)
        {
            //var argument = $"recycle/stop/start apppool /apppool.name:\"storeapi\"";
            var argument = $"{(type == 0 ? "recyle" : type == 1 ? "start" : "stop")} apppool {appPoolName}";
            Info($"开始：{argument}");
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = @"c:\Windows\System32\inetsrv\appcmd.exe",
                    Arguments = argument,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(info);
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                WriteRed($"->[{argument}]{ex.Message}", ex);
            }
        }
        #endregion
        #region 启停IIS站点
        /// <summary>
        /// 启停IIS站点
        /// </summary>
        private static void StartStopSite(string siteName, bool start)
        {
            var argument = $"{(start ? "start" : "stop")} site \"{siteName}\"";
            Info($"开始：{argument}");
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = @"c:\Windows\System32\inetsrv\appcmd.exe",
                    Arguments = argument,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                Process.Start(info);
                Thread.Sleep(1000);
            }
            catch (Exception ex)
            {
                WriteRed($"->[{argument}]{ex.Message}", ex);
            }
        }
        #endregion
        #region 创建IIS站点
        /// <summary>
        /// 创建IIS站点
        /// </summary>
        /// <param name="siteName">站点名称</param>
        /// <param name="appPoolName">程序池名称</param>
        /// <param name="path">站点文件路径</param>
        /// <param name="port">端口</param>
        /// <param name="hostName">主机名</param>
        /// <param name="certName">证书存储的名称</param>
        /// 
        /// <param name="mode">0：集成模式，1：经典模式</param>
        /// <param name="clrVersion">0：无托管代码，1：.NET CLR v4.0，2：.NET CLR v2.0</param>
        /// <param name="disallowOverlappingRotation">禁用重叠回收，若程序不支持多实例运行则设置为true</param>
        /// <param name="queueLength">队列长度</param>
        /// <param name="restartMinutes">回收间隔分钟数</param>
        /// <param name="restartTimes">回收时间</param>
        /// <param name="maxProcesses">最大进程数</param>
        private static bool CreateIISSite(IISSiteConf iisConfig, string path, int port)
        {
            try
            {
                if (iisConfig == null || iisConfig.AppPool==null)
                {
                    WriteRed($"缺少 iis/appPool 配置");
                    return false;
                }
                var service = ServiceController.GetServices("127.0.0.1").FirstOrDefault(x => string.Compare(x.ServiceName, "W3SVC", true) == 0);
                if (service is null)
                {
                    WriteYellow("先安装 IIS 服务模块 [ 控制面板-所有控制面板项-程序和功能-启用或关闭 Windows 功能：Internet Information Services ]");
                    return default;
                }
                var serverManager = new ServerManager();
                var site = serverManager.Sites[iisConfig.SiteName];
                if (site != null) return true;
                if (!CreateAppPool(iisConfig.AppPool)) { return default; }
                if (!Directory.Exists(path))
                {
                    WriteYellow("站点目录不存在");
                    return default;
                }
                site = serverManager.Sites.Add(iisConfig.SiteName, path, port);
                site.ServerAutoStart = true;
                site.Applications["/"].ApplicationPoolName = iisConfig.AppPool.AppPoolName;
                site.Applications["/"].SetAttributeValue("preloadEnabled", iisConfig.PreloadEnabled);
                string bindPrev = site.Bindings[0].Host.Split(new char[] { '.' })[0];
                if (!string.IsNullOrWhiteSpace(iisConfig.DomainName))
                {
                    if (string.IsNullOrWhiteSpace(iisConfig.CertName) && string.IsNullOrWhiteSpace(iisConfig.CertFile))
                    {
                        var bindingInformation = $"*:80:{iisConfig.DomainName}";
                        site.Bindings.Add(bindingInformation, "http");
                    }
                    else
                    {
                        var bindingInformation = $"*:443:{iisConfig.DomainName}";
                        var cert = string.IsNullOrWhiteSpace(iisConfig.CertName) ? CertInfo(iisConfig.CertFile, iisConfig.CertPwd) : GetCertificatesHash(iisConfig.CertName, true);
                        site.Bindings.Add(bindingInformation, cert.Item1, cert.Item2, SslFlags.None);
                    }
                }
                serverManager.CommitChanges();
                return true;
            }
            catch (Exception ex)
            {
                WriteRed("创建iis站点出错", ex);
                return default;
            }
        }
        private static Tuple<byte[], string> CertInfo(string pfxFile, string pfxPwd)
        {
            var certificate = new X509Certificate2(pfxFile, pfxPwd, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            var certificateHash = certificate.GetCertHash();//证书哈希

            var store = new X509Store(StoreName.AuthRoot, StoreLocation.LocalMachine);
            store.Open(OpenFlags.OpenExistingOnly | OpenFlags.ReadWrite);
            store.Add(certificate);
            var certificateName = store.Name;//证书名称
            store.Close();

            return new Tuple<byte[], string>(certificateHash, certificateName);
        }

        private static Tuple<byte[], string> GetCertificatesHash(string certName, bool validOnly = true)
        {
            if (string.IsNullOrWhiteSpace(certName)) return default;
            var store = new X509Store(StoreName.My);
            try
            {
                store.Open(OpenFlags.ReadOnly);
                X509Certificate2Collection signingCerts = FindCertificate(store.Certificates, certName, validOnly);
                if (!signingCerts.Any())
                {
                    store = new X509Store(StoreName.Root);
                    store.Open(OpenFlags.ReadOnly);
                    signingCerts = FindCertificate(store.Certificates, certName, validOnly);
                }
                return new Tuple<byte[], string>(signingCerts[0].GetCertHash(), signingCerts[0].Subject);
            }
            finally
            {
                store?.Close();
            }
        }
        private static X509Certificate2Collection FindCertificate(X509Certificate2Collection certificates, string certName, bool validOnly = true)
        {
            if (certificates == null) return default;
            //CN=*.domain.com, O=xxxx有限公司, L=杭州市, S=浙江省, C=CN
            var curCertificates = certificates.Find(X509FindType.FindBySubjectDistinguishedName, certName, validOnly);
            //*.domain.com
            certificates = curCertificates.Any() ? curCertificates : certificates.Find(X509FindType.FindBySubjectName, certName, validOnly);
            certificates = certificates.Find(X509FindType.FindByTimeValid, DateTime.Now, validOnly);
            return certificates;
        }
        #endregion
        #region 创建应用程序池
        /// <summary>
        /// 创建应用程序池
        /// </summary>
        /// <param name="appPoolName">程序池名称</param>
        /// <param name="mode">0：集成模式，1：经典模式</param>
        /// <param name="clrVersion">0：无托管代码，1：.NET CLR v4.0，2：.NET CLR v2.0</param>
        /// <param name="disallowOverlappingRotation">禁用重叠回收，若程序不支持多实例运行则设置为true</param>
        /// <param name="queueLength">队列长度</param>
        /// <param name="restartMinutes">回收间隔分钟数</param>
        /// <param name="restartTimes">回收时间</param>
        /// <param name="maxProcesses">最大进程数</param>
        private static bool CreateAppPool(AppPoolConf appPoolConf)
        {
            try
            {
                if (appPoolConf == null)
                {
                    WriteRed($"缺少 appPool 配置");
                    return false;
                }
                var serverManager = new ServerManager();
                ApplicationPool appPool = serverManager.ApplicationPools[appPoolConf.AppPoolName];
                //if (appPool != null) serverManager.ApplicationPools.Remove(appPool);
                if (appPool != null) return true;
                serverManager.ApplicationPools.Add(appPoolConf.AppPoolName);
                appPool = serverManager.ApplicationPools[appPoolConf.AppPoolName];
                appPool.StartMode = appPoolConf.StartMode == 0 ? StartMode.OnDemand : StartMode.AlwaysRunning;
                if (appPoolConf.ManagedPipelineMode == 0)
                {
                    appPool.ManagedPipelineMode = ManagedPipelineMode.Integrated;//集成模式托管
                }
                else
                {
                    appPool.ManagedPipelineMode = ManagedPipelineMode.Classic;//经典模式托管 
                }
                appPool.QueueLength = appPoolConf.QueueLength;
                if (appPoolConf.ManagegRuntimeVersion == 1)
                {
                    appPool.ManagedRuntimeVersion = "v4.0";//当设置不存在的版本，不会报错,应先检测是否安装 FrameWork 4.0
                }
                else if (appPoolConf.ManagegRuntimeVersion == 2)
                {
                    appPool.ManagedRuntimeVersion = "v2.0";
                }
                else
                {
                    appPool.ManagedRuntimeVersion = string.Empty;
                }
                appPool.Recycling.DisallowRotationOnConfigChange = appPoolConf.DisallowRotationOnConfigChange;
                appPool.Recycling.DisallowOverlappingRotation = appPoolConf.DisallowOverlappingRotation;
                appPool.Recycling.PeriodicRestart.Time = TimeSpan.FromMinutes(appPoolConf.RestartMinutes);//回收间隔分钟数
                if (appPoolConf.RestartTimes != null)
                {
                    foreach (var ts in appPoolConf.RestartTimes)
                    {
                        appPool.Recycling.PeriodicRestart.Schedule.Add(ts);
                    }
                }
                appPool.Recycling.LogEventOnRecycle = RecyclingLogEventOnRecycle.Memory
                    | RecyclingLogEventOnRecycle.Requests
                    | RecyclingLogEventOnRecycle.ConfigChange
                    | RecyclingLogEventOnRecycle.IsapiUnhealthy
                    | RecyclingLogEventOnRecycle.OnDemand
                    | RecyclingLogEventOnRecycle.PrivateMemory
                    | RecyclingLogEventOnRecycle.Schedule
                    | RecyclingLogEventOnRecycle.Time;
                appPool.Recycling.PeriodicRestart.Memory = 40960000;
                appPool.Recycling.PeriodicRestart.PrivateMemory = 0;

                appPool.ProcessModel.IdleTimeout = TimeSpan.FromMinutes(0);
                appPool.ProcessModel.MaxProcesses = appPoolConf.MaxProcesses;
                appPool.ProcessModel.ShutdownTimeLimit = TimeSpan.FromSeconds(90);//关闭时间限制设置为90秒

                appPool.Cpu.Limit = 80000;
                appPool.Cpu.Action = ProcessorAction.KillW3wp;
                appPool.Failure.RapidFailProtection = false;
                appPool.AutoStart = true;
                serverManager.CommitChanges();
                //Thread.Sleep(1000);
                //appPool.Recycle();
            }
            catch (Exception ex)
            {
                WriteRed("创建应用程序池出错", ex);
                return false;
            }
            return true;
        }
        #endregion

        #region 服务更新
        /// <summary>
        /// 服务更新
        /// </summary>
        /// <remarks>
        private static void UpdateService(UpdateMode updateMode, UpdateServiceConf service, string zipFile, string deployPath, string updatePath, int reTry = 1)
        {
            if (reTry < 1) return;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                if (updateMode == UpdateMode.Cold)
                {
                    ColdUpdate(service, zipFile, updatePath);
                }
                else
                {
                    HotUpdate(updateMode, service, zipFile, deployPath, updatePath);
                }
            }
            catch (Exception ex)
            {
                WriteRed($"{service.ServiceName}->[{nameof(HotUpdate)}]{ex.Message}", ex);
                HotUpdate(updateMode, service, zipFile, deployPath, updatePath, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{service.ServiceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        } 
        #endregion

        #region Service-冷更新：原文件重命名-->更新文件-->停止服务-->启动服务
        /// <summary>
        /// Service-冷更新：原文件重命名-->更新文件-->停止服务-->启动服务
        /// </summary>
        /// <param name="service">服务配置</param>
        /// <param name="zipFile">更新包</param>
        /// <param name="updatePath">更新路径</param>
        /// <param name="reTry">重试次数</param>
        private static void ColdUpdate(UpdateServiceConf service, string zipFile, string updatePath, int reTry = 1)
        {
            if (reTry < 0) return;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                CreateService(updatePath, service.ServiceName, service.ExecuteFileName, service.ServiceDescription);
                RenameTargetFile(zipFile, updatePath, service.ServiceName);
                Info($"->{zipFile}->解压Zip文件至{updatePath}...");
                ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                using (var sc = new ServiceController(service.ServiceName))
                {
                    if (!StopService(sc)) return;//停止服务
                    StartService(sc);//启动服务
                    new Thread(delegate () { DelTmpFile(updatePath, 10, false); }) { IsBackground = true }.Start();
                }
            }
            catch (Exception ex)
            {
                WriteRed($"{service.ServiceName}->[{nameof(ColdUpdate)}]{ex.Message}", ex);
                Info($"{service.ServiceName}->等待重试...");
                Thread.Sleep(2000);
                ColdUpdate(service, zipFile, updatePath, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                WriteGreen($"[{service.ServiceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        #endregion

        #region Service/Nginx-热更新
        /// <summary>
        /// Service/Nginx-热更新
        /// </summary>
        /// <param name="updateMode">updateMode</param>
        /// <param name="service">服务配置</param>
        /// <param name="zipFile">更新包</param>
        /// <param name="deployPath">更新路径</param>
        /// <param name="updatePath">更新路径</param>
        /// <param name="reTry">重试次数</param>
        private static void HotUpdate(UpdateMode updateMode, UpdateServiceConf service, string zipFile, string deployPath, string updatePath, int reTry = 1)
        {
            if (reTry < 1) return;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                //todo：主备实例文件都更新or删除服务(双目录)，避免开机自启后有旧逻辑影响
                var services = ServiceController.GetServices();
                #region A-无任何服务
                if (!services.Any(s => string.Compare(s.ServiceName, service.ServiceName, true) == 0 || string.Compare(s.ServiceName, service.ServiceName + 2, true) == 0))
                {
                    Info($"{service.ServiceName}->A-无任何服务");
                    if (!CreateService(updatePath, service.ServiceName, service.ExecuteFileName, service.ServiceDescription)) return;
                    Info($"{zipFile}->解压Zip文件至{updatePath}...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    using (var sc = new ServiceController(service.ServiceName))
                    {
                        StartService(sc);//启动服务
                    }
                    Info($"{service.ServiceName}->服务升级完成");
                    return;
                }
                #endregion
                var updServiceName = service.ServiceName + 2;
                var stopServiceName = service.ServiceName;
                #region B-无备用服务 直接安装启动备用服务
                if (!services.Any(s => string.Compare(s.ServiceName, service.ServiceName + 2, true) == 0))
                {
                    Info($"{service.ServiceName}->B-无备用服务 直接安装启动备用服务");
                    if (updateMode == UpdateMode.Hot2)
                    {
                        //双目录模式
                        updatePath = Path.Combine(deployPath, service.Path + 2);
                        CopyChildFolderFile(Path.Combine(deployPath, service.Path), updatePath, true, "log", "logs");
                        if (service.Ports.Any(p => p > 0))
                        {
                            #region 更新端口
                            var jsonConf = Path.Combine(updatePath, "appsettings.json");
                            string jsonString = File.ReadAllText(jsonConf, Encoding.UTF8);
                            jsonString = jsonString.Replace($":{service.Ports[0]}", $":{service.BakPorts[0]}");
                            jsonString = jsonString.Replace($"{service.Ports[1]}", $"{service.BakPorts[1]}");

                            //var lines = jsonString.ToString().Split("\n");
                            //var count = 2;
                            //for (var ln = 0; ln < lines.Length; ln++)
                            //{
                            //    if (count <= 0) break;
                            //    if (lines[ln].Contains("urls"))
                            //    {
                            //        lines[ln] = lines[ln].Replace(":5000", ":5002");
                            //        count--;
                            //        continue;
                            //    }
                            //    if (lines[ln].Contains("Port"))
                            //    {
                            //        lines[ln] = lines[ln].Replace("7000", ":7001");
                            //        count--;
                            //        continue;
                            //    }
                            //}
                            //jsonString = string.Join(string.Empty, lines); 
                            File.WriteAllText(jsonConf, jsonString, Encoding.UTF8);
                            #endregion
                        }
                    }
                    else
                    {
                        DelTmpFile(updatePath);
                        RenameTargetFile(zipFile, updatePath, service.ServiceName);
                    }
                    if (!CreateService(updatePath, updServiceName, service.ExecuteFileName, service.ServiceDescription)) return;
                    Thread.Sleep(1000);
                    StartBakService(updateMode, service, zipFile, updatePath, updServiceName, stopServiceName);
                    return;
                }
                #endregion

                #region C-判断正在使用的服务实例
                Info($"{service.ServiceName}->C-判断正在使用的服务实例");
                if (!string.IsNullOrWhiteSpace(service.NginxConf))
                {
                    #region 判断正在使用的服务实例：通过nginx主备代理判断，针对用nginx代理的多实例服务
                    string oldNginxConf = Path.Combine(Configuration["NginxConfPath"] + string.Empty, service.NginxConf);
                    var text = File.ReadAllText(oldNginxConf, Encoding.UTF8);
                    if (text.Contains($":{service.Ports[1]} backup;"))
                    {
                        //备用服务使用中，更新启用主服务，停用备用服务
                        updServiceName = service.ServiceName;
                        stopServiceName = service.ServiceName + 2;
                    }
                    //foreach (var line in text.ToString().Split("\n"))
                    //{
                    //    if (line.Contains($":{service.Ports[1]} ") && line.Contains("backup"))
                    //    {
                    //        updServiceName = service.ServiceName;
                    //        stopServiceName = service.ServiceName + 2;
                    //        break;//备用服务使用中，更新启用主服务，停用备用服务
                    //    }
                    //    if (line.Contains($":{service.BakPorts[1]} ") && line.Contains("backup"))
                    //    {
                    //        break;//主服务使用中，更新启用备用服务，停用主服务
                    //    }
                    //}
                    #endregion
                }
                else
                {
                    #region 判断新旧实例-无端口：通过[运行状态+运行时长]判断，针对无nginx代理的实例
                    var mainStatus = services.Where(s => s.ServiceName == service.ServiceName).Select(d => d.Status).FirstOrDefault();
                    var bakStatus = services.Where(s => s.ServiceName == updServiceName).Select(d => d.Status).FirstOrDefault();
                    if (mainStatus == ServiceControllerStatus.Running && bakStatus == ServiceControllerStatus.Running)
                    {
                        var mainPid = GetPidByServiceName2(service.ServiceName);
                        var bakPid = GetPidByServiceName2(updServiceName);
                        var mainProcess = Process.GetProcessById(mainPid);
                        var bakProcess = Process.GetProcessById(bakPid);
                        if (mainProcess.StartTime < bakProcess.StartTime)
                        {
                            //新旧实例都在运行，备用实例使用中，更新启用主实例
                            updServiceName = service.ServiceName;
                            stopServiceName = service.ServiceName + 2;
                        }
                    }
                    else if (mainStatus != ServiceControllerStatus.Running && bakStatus != ServiceControllerStatus.Running)
                    {
                        //新旧实例都未运行，更新启用主实例
                        updServiceName = service.ServiceName;
                        stopServiceName = service.ServiceName + 2;
                    }
                    else if (bakStatus == ServiceControllerStatus.Running)
                    {
                        //备用实例运行使用中，更新启用主实例
                        updServiceName = service.ServiceName;
                        stopServiceName = service.ServiceName + 2;
                    }
                    //else
                    //{
                    //    //新旧实例都在运行+主实例使用中 or 主实例使用中，更新启用备用实例
                    //    updServiceName = service.ServiceName + 2;
                    //    stopServiceName = service.ServiceName;
                    //}
                    #endregion
                }
                Info($"更新启用{updServiceName}，停用备用服务{stopServiceName}...");
                CreateService(updatePath, updServiceName, service.ExecuteFileName, service.ServiceDescription);
                using (var sc = new ServiceController(updServiceName))
                {
                    if (!StopService(sc)) return;//停止服务
                    //Thread.Sleep(20000);
                }
                if (updateMode == UpdateMode.Hot2)
                {
                    //双目录模式
                    updatePath = Path.Combine(deployPath, updServiceName == service.ServiceName ? service.Path : service.Path + 2);
                }
                else
                {
                    DelTmpFile(updatePath);
                    RenameTargetFile(zipFile, updatePath, service.ServiceName);
                }
                StartBakService(updateMode, service, zipFile, updatePath, updServiceName, stopServiceName);
                return;
                #endregion
            }
            catch (Exception ex)
            {
                WriteRed($"{service.ServiceName}->[{nameof(HotUpdate)}]{ex.Message}", ex);
                HotUpdate(updateMode, service, zipFile, deployPath, updatePath, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{service.ServiceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        private static void StartBakService(UpdateMode updateMode, UpdateServiceConf service, string zipFile, string updatePath, string startServiceName, string stopServiceName)
        {
            Info($"{zipFile}->解压Zip文件至{updatePath}...");
            ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
            //启动新服务
            using (var sc = new ServiceController(startServiceName))
            {
                StartService(sc);//启动服务
                SetAutoStart(sc.ServiceName, true);//允许自启
            }
            #region 更新重载nginx设置（若用nginx）
            if (updateMode != UpdateMode.Cold && !string.IsNullOrWhiteSpace(service.NginxConf))
            {
                var UpdateNginxResult = false;
                try
                {
                    var oldPorts = startServiceName.EndsWith("2") ? service.Ports : service.BakPorts;
                    var newPorts = stopServiceName.EndsWith("2") ? service.Ports : service.BakPorts;
                    UpdateNginxResult = ReloadNginxConf(oldPorts, newPorts, service);
                }
                catch (Exception ex)
                {
                    WriteRed($"更新nginx配置失败", ex);
                }
                if (!UpdateNginxResult)
                {
                    using (var sc = new ServiceController(startServiceName))
                    {
                        StopService(sc);
                    }
                    SetAutoStart(startServiceName, false);
                    return;
                }
            }
            #endregion
            SetAutoStart(stopServiceName, false);
            #region 停止服务+删除服务
            if (service.KillOldWaitSeconds > 0)
            {
                new Thread(delegate ()
                {
                    var waitSeconds = service.KillOldWaitSeconds - 1;
                    Thread.Sleep(waitSeconds * 1000);
                    if (ServiceController.GetServices().Any(s => s.ServiceName == stopServiceName))
                    {
                        using (var sc = new ServiceController(stopServiceName))
                        {
                            StopService(sc, 1);
                        }
                        //DeleteService(stopServiceName);
                    }
                })
                { IsBackground = true }.Start();
            }
            #endregion
            Info($"{stopServiceName}->清理重命名文件...");
            DelTmpFile(updatePath, 3);
            Info($"{stopServiceName}->服务升级完成");
        }
        #endregion

        #region 创建服务
        private static bool CreateService(string updatePath, string serviceName, string executeFileName, string serviceDescription = null)
        {
            try
            {
                if (ServiceController.GetServices().Any(s => s.ServiceName == serviceName)) return false;
                var cmdCreate = $"create {serviceName} binpath= {Path.Combine(updatePath, executeFileName + ".exe")} start= auto";
                Info($"{serviceName}->服务创建中[sc {cmdCreate}]...");
                Info($">sc {cmdCreate}");
                StartProcess("sc", cmdCreate, false);
                var cmdDescription = $"description {serviceName} {(string.IsNullOrWhiteSpace(serviceDescription) ? serviceName : serviceDescription)}";
                Info($">sc {cmdDescription}");
                StartProcess("sc", cmdDescription, false);
                Thread.Sleep(1000);
                return true;
            }
            catch (Exception ex)
            {
                WriteRed($"{serviceName}->服务创建出错", ex);
                return false;
            }
        }
        #endregion
        #region 启动服务
        private static void StartService(ServiceController sc)
        {
            Info($"{sc.ServiceName}->服务启动中...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running);
            Info($"{sc.ServiceName}->服务启动完成");
        } 
        #endregion
        #region 停止服务
        private static bool StopService(ServiceController sc, int waitSeconds = 9)
        {
            var result = true;
            Info($"{sc.ServiceName}->状态：{sc.Status}");
            try
            {
                if (sc.Status != ServiceControllerStatus.Stopped)
                {
                    Info($"{sc.ServiceName}->服务停止中(最多1~{10}秒)...");
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(waitSeconds));
                    Info($"{sc.ServiceName}->服务已停止");
                }
            }
            catch (System.ServiceProcess.TimeoutException tex)
            {
                Info($"{sc.ServiceName}->服务停止超时，放大招...");
                result = false;
            }
            catch (Exception ex)
            {
                Info($"{sc.ServiceName}->{ex.Message}");
                result = false;
            }
            finally
            {
                if (!result)
                {
                    Info($">sc queryex {sc.ServiceName}");
                    var pid = GetPidByServiceName(sc.ServiceName);
                    if (pid > 0)
                    {
                        var taskkill = $"/PID {pid} /F";
                        Info($">taskkill {taskkill}");
                        StartProcess("taskkill", taskkill, false);
                        result = true;
                    }
                    else
                    {
                        WriteYellow($"没取到pid，手动介入下...");
                    }
                }
            }
            return result;
        }
        #endregion
        #region 删除服务
        private static void DeleteService(string serviceName)
        {
            Info($"{serviceName}->删除服务");
            var cmdDelete = $"delete {serviceName}";
            Info($">sc {cmdDelete}");
            StartProcess("sc", cmdDelete, false);
            Info($"{serviceName}->服务已删除");
        }
        #endregion
        #region 设置服务自启状态
        private static void SetAutoStart(string serviceName, bool autoStart = true)
        {
            Info($"{serviceName}->设置服务自启状态-{autoStart}");
            var cmd = $"config {serviceName} start= {(autoStart ? "auto" : "demand")}";
            Info($">sc {cmd}");
            StartProcess("sc", cmd, false);
        }
        #endregion

        #region 获取pid-根据端口
        /// <summary>
        /// 获取pid-根据端口
        /// </summary>
        /// <returns>pid</returns>
        /// <remarks>
        /// <br/>https://learn.microsoft.com/zh-cn/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc754599(v=ws.11)
        /// <br/>https://learn.microsoft.com/zh-cn/windows/win32/services/configuring-a-service-using-sc
        /// <br/>https://learn.microsoft.com/zh-cn/windows-server/administration/windows-commands/sc-query
        /// </remarks>
        private static int GetPidByPort(int port)
        {
            var output = StartProcess("netstat", "-a -n -o", true);
            var pidStr = GetPidFromOutputByPort(output, port);
            _ = int.TryParse(pidStr, out var pid);
            return pid;
        }
        #endregion
        #region 获取pid-根据服务名称
        /// <summary>
        /// 获取pid-根据服务名称
        /// </summary>
        /// <returns>pid</returns>
        /// <remarks>
        /// <br/>https://learn.microsoft.com/zh-cn/previous-versions/windows/it-pro/windows-server-2012-r2-and-2012/cc754599(v=ws.11)
        /// <br/>https://learn.microsoft.com/zh-cn/windows/win32/services/configuring-a-service-using-sc
        /// <br/>https://learn.microsoft.com/zh-cn/windows-server/administration/windows-commands/sc-query
        /// </remarks>
        private static int GetPidByServiceName(string serviceName)
        {
            var result = StartProcess("sc", $"queryex {serviceName}", true);
            if (!string.IsNullOrWhiteSpace(result))
            {
                string line = result.Split('\n').Where(r => r.Contains("PID")).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string[] items = line.Split('\t', '\r', ' ').Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                    _ = int.TryParse(items[2], out var pid);
                    return pid;
                }
            }
            return default;
        }
        #endregion
        #region 获取pid-根据服务名称
        /// <summary>
        /// 获取pid-根据服务名称
        /// </summary>
        /// <returns>pid</returns>
        /// <remarks>https://stackoverflow.com/questions/23084720/get-the-pid-of-a-windows-service</remarks>
        private static int GetPidByServiceName2(string serviceName)
        {
            var sc = ServiceController.GetServices().Where(s => s.ServiceName == serviceName).FirstOrDefault();
            if (sc == null) return default;

            IntPtr zero = IntPtr.Zero;
            try
            {
                int errorInsufficientBuffer = 0x7a;
                int scStatusProcessInfo = 0;
                // Call once to figure the size of the output buffer.
                QueryServiceStatusEx(sc.ServiceHandle, scStatusProcessInfo, zero, 0, out uint dwBytesNeeded);
                if (Marshal.GetLastWin32Error() == errorInsufficientBuffer)
                {
                    // Allocate required buffer and call again.
                    zero = Marshal.AllocHGlobal((int)dwBytesNeeded);
                    if (QueryServiceStatusEx(sc.ServiceHandle, scStatusProcessInfo, zero, dwBytesNeeded, out dwBytesNeeded))
                    {
                        var ssp = new SERVICE_STATUS_PROCESS();
                        Marshal.PtrToStructure(zero, ssp);
                        return (int)ssp.dwProcessId;
                    }
                }
            }
            finally
            {
                if (zero != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(zero);
                }
            }
            return default;
        }
        #endregion

        #region 根据指定的信息级别检索指定服务的当前状态
        /// <summary>
        /// 根据指定的信息级别检索指定服务的当前状态。
        /// </summary>
        /// <param name="hService">句柄</param>
        /// <param name="infoLevel">服务属性。使用SC_STATUS_PROCESS_INFO检索服务状态信息。</param>
        /// <param name="lpBuffer">指向接收状态信息的缓冲区（SERVICE_STATUS_PROCESS结构体）的指针。</param>
        /// <param name="cbBufSize">指向的缓冲区的大小，以字节为单位。</param>
        /// <param name="pcbBytesNeeded">变量的指针，所有状态信息所需的字节数。</param>
        /// <returns></returns>
        /// <remarks>https://stackoverflow.com/questions/23084720/get-the-pid-of-a-windows-service</remarks>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool QueryServiceStatusEx(SafeHandle hService, int infoLevel, IntPtr lpBuffer, uint cbBufSize, out uint pcbBytesNeeded);
        //[DllImport("advapi32.dll", SetLastError = true)]
        //internal static extern bool QueryServiceStatusEx(SafeHandle hService, int infoLevel, IntPtr lpBuffer, uint cbBufSize, out uint pcbBytesNeeded); 
        #endregion

        #region 获取按端口查找的命令输出结果
        private static string GetPidFromOutputByPort(string output, int port)
        {
            string[] rows = Regex.Split(output, "\r\n");
            foreach (string row in rows)
            {
                string[] strs = Regex.Split(row, "\\s+");
                foreach (string str in strs)
                {
                    if (str.EndsWith($":{port}"))
                    {
                        return strs[strs.Length - 1];
                    }
                }
            }
            return string.Empty;
        }
        #endregion
        #region 获取指定pid对应的进程名
        private static string GetProcessNameByPid(int pid)
        {
            return Process.GetProcessById(pid).ProcessName;
        }
        #endregion

        #region 文件是否被占用 Win32Api调用
        [DllImport("kernel32.dll")]
        private static extern IntPtr _lopen(string lpPathName, int iReadWrite);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);
        private const int OF_READWRITE = 2;//文件打开的模式-读写
        private const int OF_SHARE_DENY_NONE = 0x40;//文件打开的模式-共享读写
        private static readonly IntPtr HFILE_ERROR = new IntPtr(-1);//文件打开错误标志位，用于判断文件打开后的状态判断
        /// <summary>
        /// 文件是否被占用 Win32Api调用
        /// </summary>
        /// <param name="fileFullName">文件的完全限定名</param>
        /// <returns>正被占用为true,否则为false </returns>
        private static bool IsOccupied(string fileFullName)
        {
            if (!File.Exists(fileFullName)) return false;
            IntPtr vHandle = _lopen(fileFullName, OF_READWRITE | OF_SHARE_DENY_NONE);
            var flag = vHandle == HFILE_ERROR;
            CloseHandle(vHandle);
            return flag;
        }
        #endregion

        #region 端口是否被占用
        private static Tuple<bool, bool> IsUsedPort(int port)
        {
            IPGlobalProperties properties = IPGlobalProperties.GetIPGlobalProperties();
            // 遍历TCP端口信息，找到指定端口号
            var tcpPort = false;
            foreach (IPEndPoint endPoint in properties.GetActiveTcpListeners())
            {
                if (endPoint.Port == port)
                {
                    tcpPort = true;
                    break;
                }
            }
            // 遍历UDP端口信息，找到指定端口号
            var udpPort = false;
            foreach (IPEndPoint endPoint in properties.GetActiveUdpListeners())
            {
                if (endPoint.Port == port)
                {
                    udpPort = true;
                    break;
                }
            }
            return new Tuple<bool, bool>(tcpPort, udpPort);
        }
        #endregion
    }

    #region 服务的进程状态信息
    [StructLayout(LayoutKind.Sequential)]
    internal sealed class SERVICE_STATUS_PROCESS
    {
        [MarshalAs(UnmanagedType.U4)]
        public uint dwServiceType;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwCurrentState;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwControlsAccepted;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwWin32ExitCode;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwServiceSpecificExitCode;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwCheckPoint;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwWaitHint;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwProcessId;
        [MarshalAs(UnmanagedType.U4)]
        public uint dwServiceFlags;
    } 
    #endregion
}
