using System.Diagnostics;
using System.IO.Compression;
using System.Net.NetworkInformation;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;

namespace Yu.DotnetUpdater
{
    /// <summary>
    /// 部署至Windows服务/IIS
    /// </summary>
    /// <remarks>
    /// 更新配置文件-->检测更新包-->备份项目原文件-->根据项目类型执行更新：<br/>
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    //[System.Runtime.Versioning.UnsupportedOSPlatform("linux")]
    //[System.Runtime.Versioning.UnsupportedOSPlatform("maccatalyst")]
    //[System.Runtime.Versioning.UnsupportedOSPlatform("tvos")]
    //[System.Runtime.Versioning.UnsupportedOSPlatform("ios")]
    internal class DeployWindows : Util
    {
        #region StartForWindow
        /// <summary>
        /// StartForWindow
        /// </summary>
        /// <param name="updateIndexs">待更新服务Services索引</param>

        internal static void Start(int[] updateIndexs, UpdateServiceConf[] services)
        {
            var stopwatch = new Stopwatch();
            var deployPath = Configuration["DeployPath"];
            for (var i = 0; i < services.Length; i++)
            {
                if (!updateIndexs.Contains(i)) continue;
                stopwatch.Reset();
                stopwatch.Start();
                if (i > 0) Info(string.Empty);

                #region 查找待更新文件
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].UpdatePack}->查找待更新文件...");
                string zipFile = Path.Combine(ToolRunPath, services[i].UpdatePack);
                if (!File.Exists(zipFile))
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].UpdatePack}->未找到待更新压缩包 : {zipFile}");
                    continue;
                }
                #endregion
                string updatePath = Path.Combine(deployPath, services[i].Path);
                #region 备份原文件
                if (UpdateServiceConf.BakDirectoryNameDemo.Contains(services[i].BakDirectoryFormat))
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->备份原文件...");
                    string bakPathBase = Path.Combine(deployPath, "bak");
                    string pathBak = Path.Combine(bakPathBase, DateTime.Now.ToString(services[i].BakDirectoryFormat));
                    CreateFolder(updatePath);
                    CopyFolderFile(updatePath, pathBak, true, "logs", "log");

                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->....................................");
                    DeleteOldBak(bakPathBase);
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->备份原文件完成.");
                }
                #endregion
                var updateMode = (UpdateMode)services[i].UpdateMode;
                Info($"[{DateTime.Now:HH:mm:ss.fff}]更新模式：{updateMode}");
                if (!string.IsNullOrWhiteSpace(services[i].AppPool))
                {
                    IISSiteUpdate(services[i], zipFile, updatePath, updateMode);
                }
                else
                {
                    if (updateMode == UpdateMode.Cold)
                    {
                        ColdUpdate(services[i], zipFile, updatePath);
                    }
                    else
                    {
                        HotUpdate(updateMode, services[i], zipFile, deployPath, updatePath);
                    }
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
                stopwatch.Start();
                if (mode != UpdateMode.Cold)
                {
                    RenameTargetFile(zipFile, updatePath, service.ServiceName);
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.UpdatePack}->解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    ApppoolStatusUpdate(service.AppPool, 0);
                    new Thread(delegate () { DelTmpFile(updatePath, 10, false); }) { IsBackground = true }.Start();
                }
                else
                {
                    ApppoolStatusUpdate(service.AppPool, 2);
                    StartStopSite(service.SiteName, false);
                    Thread.Sleep(2000);
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.UpdatePack}->解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    ApppoolStatusUpdate(service.AppPool, 1);
                    StartStopSite(service.SiteName, true);
                }
                if (!string.IsNullOrWhiteSpace(service.OpenUrl))
                {
                    Thread.Sleep(2000);
                    Task.Run(async () =>
                    {
                        Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.UpdatePack}->请求一次：{service.OpenUrl}");
                        try
                        {
                            using var client = new HttpClient();
                            var resp = await client.PostAsync(service.OpenUrl, default);
                            await resp.Content.ReadAsStringAsync();
                            //client.PostAsync(service.OpenUrl, default).ContinueWith(res => res.Result.Content.ReadAsStringAsync().Wait(10000));
                        }
                        catch (Exception ex)
                        {
                            WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]请求url出错[{service.OpenUrl}]{ex.Message}]{ex}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{service.UpdatePack}->[{nameof(IISSiteUpdate)}]{ex}");
            }
            finally
            {
                stopwatch.Stop();
                WriteGreen($"[{service.UpdatePack}]耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        /// <summary>
        /// 更新IIS应用程序池状态
        /// </summary>
        /// <param name="appPoolName">程序池名</param>
        /// <param name="type">操作类型 0-回收，1-启动，2-停止</param>
        private static void ApppoolStatusUpdate(string appPoolName, int type)
        {
            //var argument = $"recycle/stop/start apppool /apppool.name:\"storeapi\"";
            var argument = $"{(type == 0 ? "recyle" : type == 1 ? "start" : "stop")} apppool {appPoolName}";
            Info($"[{DateTime.Now:HH:mm:ss.fff}]开始：{appPoolName}");
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
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]->[{argument}]{ex.Message}");
            }
            finally
            {
                Info($"[{DateTime.Now:HH:mm:ss.fff}]结束：{argument}");
            }
        }
        /// <summary>
        /// 启停IIS站点
        /// </summary>
        private static void StartStopSite(string siteName, bool start)
        {
            var argument = $"{(start ? "start" : "stop")} site \"{siteName}\"";
            Info($"[{DateTime.Now:HH:mm:ss.fff}]开始：{argument}");
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
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]->[{argument}]{ex.Message}");
            }
            finally
            {
                Info($"[{DateTime.Now:HH:mm:ss.fff}]结束：{argument}");
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
                Info($"[{DateTime.Now:HH:mm:ss.fff}]->{zipFile}->解压Zip文件至{updatePath}...");
                ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                #region 初次部署：针对在同一主机部署多套服务，目录不同但执行文件一致
                if (service.ServiceName != service.ExecuteFileName)
                {
                    string oldPath = Path.Combine(Configuration["DeployPath"]!, service.ExecuteFileName);
                    string[] jsons = Directory.GetFiles(oldPath, "*.json");
                    foreach (string file in jsons)
                    {
                        var curName = Path.GetFileName(file);
                        var filePath = Path.Combine(updatePath, curName);
                        if (!File.Exists(filePath)) File.Copy(file, filePath);
                    }
                    string[] configs = Directory.GetFiles(oldPath, "*.config");
                    foreach (string file in configs)
                    {
                        var curName = Path.GetFileName(file);
                        var filePath = Path.Combine(updatePath, curName);
                        if (!File.Exists(filePath)) File.Copy(file, filePath);
                    }
                }
                #endregion
                using (var sc = new ServiceController(service.ServiceName))
                {
                    if (!StopService(sc)) return;//停止服务
                    StartService(sc);//启动服务
                    new Thread(delegate () { DelTmpFile(updatePath, 10, false); }) { IsBackground = true }.Start();
                }
            }
            catch (Exception ex)
            {
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->[{nameof(ColdUpdate)}]{ex.Message}");
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->等待重试...");
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

        #region Service/Nginx-热更新(双实例服务)
        /// <summary>
        /// Service/Nginx-热更新(双实例服务)
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
            string serviceName = service.ServiceName;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                #region 第一次 无任何服务
                if (!ServiceController.GetServices().Any(s => s.ServiceName == serviceName))
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->第一次 无任何服务");
                    if (!CreateService(updatePath, serviceName, service.ExecuteFileName, service.ServiceDescription)) return;
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{zipFile}->解压Zip文件至{updatePath}...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    using (var sc = new ServiceController(serviceName))
                    {
                        StartService(sc);//启动服务
                    }
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->服务升级完成");
                    return;
                }
                #endregion

                var updServiceName = serviceName + 2;
                var stopServiceName = serviceName;
                #region 第二次 无备用服务 直接安装启动备用服务
                if (!ServiceController.GetServices().Any(s => s.ServiceName == updServiceName))
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->第二次 无备用服务 直接安装启动备用服务");
                    if (updateMode == UpdateMode.Hot)
                    {
                        updatePath = Path.Combine(deployPath, service.Path + 2);
                        CopyChildFolderFile(Path.Combine(deployPath, service.Path), updatePath, true, "log", "logs");
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
                    else
                    {
                        DelTmpFile(updatePath);
                        RenameTargetFile(zipFile, updatePath, serviceName);
                    }
                    if (!CreateService(updatePath, updServiceName, service.ExecuteFileName, service.ServiceDescription)) return;
                    Thread.Sleep(1000);
                    StartBakService(updateMode, service, zipFile, updatePath, updServiceName, stopServiceName);
                    return;
                }
                #endregion

                #region 第三次及以上，判断正在使用的服务实例
                if (!string.IsNullOrWhiteSpace(service.NginxConf))
                {
                    #region 判断正在使用的服务实例：通过nginx主备代理判断，针对用nginx代理的多实例服务
                    string oldNginxConf = Path.Combine(Configuration["NginxConfPath"] + string.Empty, service.NginxConf);
                    var text = File.ReadAllText(oldNginxConf, Encoding.UTF8);
                    if (text.Contains($":{service.Ports[1]} backup;"))
                    {
                        //备用服务使用中，更新启用主服务，停用备用服务
                        updServiceName = serviceName;
                        stopServiceName = serviceName + 2;
                    }
                    //foreach (var line in text.ToString().Split("\n"))
                    //{
                    //    if (line.Contains($":{service.Ports[1]} ") && line.Contains("backup"))
                    //    {
                    //        updServiceName = serviceName;
                    //        stopServiceName = serviceName + 2;
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
                    #region 判断正在使用的服务实例：通过运行状态判断，针对同时只有一个实例在运行的服务
                    if (ServiceController.GetServices().Where(s => s.ServiceName == serviceName).FirstOrDefault().Status == ServiceControllerStatus.Stopped)
                    {
                        //备用服务使用中，更新启用主服务，停用备用服务
                        updServiceName = serviceName;
                        stopServiceName = serviceName + 2;
                    }
                    else if (ServiceController.GetServices().Where(s => s.ServiceName == updServiceName).FirstOrDefault().Status == ServiceControllerStatus.Stopped)
                    {
                        //主服务使用中，更新启用备用服务，停用主服务
                        updServiceName = serviceName + 2;
                        stopServiceName = serviceName;
                    }
                    #endregion
                    #region 通过新旧端口配置判断，针对无代理的多实例服务
                    else
                    {
                        var processName = GetProcessNameByPort(service.Ports[0]);
                        if (string.IsNullOrWhiteSpace(processName))
                        {
                            //备用服务使用中，更新启用主服务，停用备用服务
                            updServiceName = serviceName;
                            stopServiceName = serviceName + 2;
                        }
                        else
                        {
                            //主服务使用中，更新启用备用服务，停用主服务
                            updServiceName = serviceName + 2;
                            stopServiceName = serviceName;
                        }
                    }
                    #endregion
                }
                Info($"[{DateTime.Now:HH:mm:ss.fff}]更新启用{updServiceName}，停用备用服务{stopServiceName}...");
                using (var sc = new ServiceController(updServiceName))
                {
                    if (!StopService(sc)) return;//停止服务
                    //Thread.Sleep(20000);
                }
                if (updateMode == UpdateMode.Hot)
                {
                    updatePath = Path.Combine(deployPath, updServiceName == serviceName ? service.Path : service.Path + 2);
                }
                else
                {
                    DelTmpFile(updatePath);
                    RenameTargetFile(zipFile, updatePath, serviceName);
                }
                StartBakService(updateMode, service, zipFile, updatePath, updServiceName, stopServiceName);
                return;
                #endregion
            }
            catch (Exception ex)
            {
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->[{nameof(HotUpdate)}]{ex.Message} {ex.InnerException?.Message}");
                HotUpdate(updateMode, service, zipFile, deployPath, updatePath, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{serviceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        private static void StartBakService(UpdateMode updateMode, UpdateServiceConf service, string zipFile, string updatePath, string startServiceName, string stopServiceName)
        {
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{zipFile}->解压Zip文件至{updatePath}...");
            ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
            //启动新服务
            using (var sc = new ServiceController(startServiceName))
            {
                StartService(sc);//启动服务
            }
            #region 更新重载nginx设置（若用nginx）
            if (updateMode == UpdateMode.Hot && !string.IsNullOrWhiteSpace(service.NginxConf))
            {
                var UpdateNginxResult = false;
                try
                {
                    var oldPort = startServiceName.EndsWith("2") ? service.Ports[1] : service.BakPorts[1];
                    var newPort = stopServiceName.EndsWith("2") ? service.Ports[1] : service.BakPorts[1];
                    UpdateNginxResult = ReloadNginxConf(oldPort, newPort, service);
                }
                catch (Exception ex)
                {
                    WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]更新nginx配置失败", ex);
                }
                if (!UpdateNginxResult)
                {
                    using (var sc = new ServiceController(startServiceName))
                    {
                        StopService(sc);//停止服务
                    }
                    DeleteService(startServiceName);//删除服务
                    return;
                }
            }
            #endregion
            #region 停止并删除旧服务及文件
            if (service.KillOldWaitSeconds > 0 && ServiceController.GetServices().Any(s => s.ServiceName == stopServiceName))
            {
                Thread.Sleep(Math.Max(service.KillOldWaitSeconds, 20) * 1000);
                using (var sc = new ServiceController(stopServiceName))
                {
                    StopService(sc);//停止服务
                }
                DeleteService(stopServiceName);//删除服务
            }
            #endregion
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{stopServiceName}->清理重命名文件...");
            DelTmpFile(updatePath, 3);
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{stopServiceName}->服务升级完成");
        }
        private static void DeleteService(string serviceName)
        {
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->删除服务");
            var cmdDelete = $"delete {serviceName}";
            Info($">sc {cmdDelete}");
            StartProcess("sc", cmdDelete, false);
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->服务已删除");
        }
        #endregion

        #region 创建服务
        private static bool CreateService(string updatePath, string serviceName, string executeFileName, string serviceDescription = null)
        {
            try
            {
                if (ServiceController.GetServices().Any(s => s.ServiceName == serviceName)) return false;
                var cmdCreate = $"create {serviceName} binpath= {Path.Combine(updatePath, executeFileName + ".exe")} start= auto";
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->服务创建中[sc {cmdCreate}]...");
                Info($">sc {cmdCreate}");
                StartProcess("sc", cmdCreate, false);
                var cmdDescription = $"description {serviceName} {(string.IsNullOrWhiteSpace(serviceDescription) ? serviceName : serviceDescription)}";
                Info($">sc {cmdDescription}");
                StartProcess("sc", cmdDescription, false);
                return true;
            }
            catch (Exception ex)
            {
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->服务创建出错", ex);
                return false;
            }
        }
        #endregion
        #region 启动服务
        private static void StartService(ServiceController sc)
        {
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->服务启动中...");
            sc.Start();
            sc.WaitForStatus(ServiceControllerStatus.Running);
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->服务启动完成");
        } 
        #endregion
        #region 停止服务
        private static bool StopService(ServiceController sc)
        {
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->状态：{sc.Status}");
            if (sc.Status != ServiceControllerStatus.Stopped)
            {
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->服务停止中(最多1~{10}秒)...");
                try
                {
                    sc.Stop();
                    sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(9));
                }
                catch// (System.ServiceProcess.TimeoutException tex)
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->服务停止超时，放大招...");
                    Info($">sc queryex {sc.ServiceName}");
                    var pid = GetPidByService(sc.ServiceName);
                    if (string.IsNullOrWhiteSpace(pid))
                    {
                        WriteYellow($"没取到pid，手动介入下...");
                        return false;
                    }
                    var taskkill = $"/PID {pid} /F";
                    Info($">taskkill {taskkill}");
                    StartProcess("taskkill", taskkill, false);
                }
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{sc.ServiceName}->服务已停止");
            }
            return true;
        }
        #endregion

        #region 获取服务pid
        /// <summary>
        /// 获取服务pid
        /// </summary>
        private static string GetPidByService(string serviceName)
        {
            var result = StartProcess("sc", $"queryex {serviceName}", true);
            if (!string.IsNullOrWhiteSpace(result))
            {
                string line = result.Split('\n').Where(r => r.Contains("PID")).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string[] items = line.Split('\t', '\r', ' ').Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                    return items[2];
                }
            }
            return string.Empty;
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
        #region 获取指定端口对应的进程名
        public static string GetProcessNameByPort(int port)
        {
            var output = StartProcess("netstat", "-a -n -o", true);
            var pidStr = GetPidFromOutputByPort(output, port);
            _ = int.TryParse(pidStr, out var pid);
            return GetProcessNameByPid(pid);
        }
        #endregion
        private static Tuple<bool, bool> IsUsePort(int port)
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
    }
}
