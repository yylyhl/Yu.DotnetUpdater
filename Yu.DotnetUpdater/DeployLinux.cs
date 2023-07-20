using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Yu.DotnetUpdater
{
    /// <summary>
    /// 部署至Linux/Nginx
    /// </summary>
    /// <remarks>
    /// 更新配置文件-->检测更新文包-->备份项目原文件-->根据项目类型执行更新：<br/>
    /// Nginx(冷更新)：更新文件-->取旧进程id-->kill旧进程&&启动进程<br/>
    /// Nginx(冷更新)：更新文件-->取旧进程端口id-->用新端口启动进程-->更新nginx代理配置-->删除旧进程<br/>
    /// Nginx(热更新)：更新文件-->取旧进程端口id-->用新端口启动进程-->调转nginx代理主备配置-->删除旧进程<br/>
    /// <br/>
    /// Daemon(冷更新)：更新文件-->取旧进程pid-->kill旧进程&&启动进程br/>
    /// Daemon(热更新)：更新文件-->取旧进程pid-->启动新进程-->关闭旧进程
    /// br/>br/>
    /// kill -15
    /// 2.   SIGNIT  (Ctrl+C)
    /// 3.   SIGQUIT （退出）
    /// 9.   SIGKILL(强制终止）【不能让程序捕获到】
    /// 15. SIGTERM （终止）
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public class DeployLinux : Util
    {
        #region StartForLinux
        public static void StartForLinux(int[] updateIndexs, UpdateServiceConf[] services)
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
                var updatePath = Path.Combine(deployPath, services[i].Path);
                #region 备份更新
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->备份更新...");
                string bakPathBase = Path.Combine(deployPath, "bak", services[i].ServiceName);
                string pathBak = Path.Combine(bakPathBase, DateTime.Now.ToString(services[i].BakDirectoryFormat));
                CreateFolder(updatePath);
                StartProcess("mkdir", $"{pathBak} -p", false);
                StartProcess("cp", $"-r {updatePath} {pathBak}", false);
                //CopyFolderFile(updatePath, pathBak);

                Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->....................................");
                DeleteOldBak(bakPathBase);
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{services[i].ServiceName}->备份更新完成."); 
                #endregion
                var updateMode = (UpdateMode)services[i].UpdateMode;
                if (services[i].Ports.Any(p => p > 0))
                {
                    WebUpdate(services[i], zipFile, updatePath, updateMode);
                }
                else
                {
                    DaemonUpdate(services[i].ServiceName, zipFile, updatePath, updateMode);
                }
                stopwatch.Stop();
                Info($"[{services[i].UpdatePack}]更新耗时:{stopwatch.ElapsedMilliseconds}ms");
                Thread.Sleep(1000);
            }
        }
        #endregion

        #region Update Docker
        private static void UpdateByDocker()
        {
            //按顺序执行：
            //1.获取已有镜像、容器
            //2.构建新镜像：cd /home/deploy/MTT.Kitchen.AlgorithmCallPy && sudo docker build -t algorithmcallpy:1.0 .
            //3.停止并删除旧容器：sudo docker stop AlgorithmGrpcCall1 && sudo docker rm AlgorithmGrpcCall1
            //4.运行新容器：sudo docker run --restart=always --name AlgorithmGrpcCall1 -p 7801:7800 --privileged=true -v /home/deploy/MTT.Kitchen.AlgorithmCallPy/Logs:/app/Logs algorithmcallpy:1.0 &
            //5.删除旧镜像：sudo docker rmi algorithmcallpy
            var workDir = "/home/deploy/MTT.Kitchen.AlgorithmCallPy";
            var dockerRunning = GetDocker("ps", "->7800");
            var dockerAll = GetDocker("ps -a", "->7800");
            var imageName = dockerRunning.FirstOrDefault().Split(',')[2];
            var imageNameNew = imageName.Replace(":new", "") + ":new";

            StartProcess("docker", $"build -t {imageNameNew} .", false);
            foreach (var docker in dockerAll)
            {
                StartProcess("docker", "stop " + docker.Split(',')[0], false);
                StartProcess("docker", "rm " + docker.Split(',')[0], false);
                StartProcess("docker", $"run --restart=always --name {docker.Split(',')[1]} -p {docker.Split(',')[3]}:7800 --privileged=true -v {workDir}/Logs:/app/Logs {imageNameNew} & ", false);
            }
            StartProcess("docker", "rmi " + imageName, false);
        }
        /// <summary>
        /// 获取docker容器数据 
        /// </summary>
        /// <param name="dockerParams">docker参数</param>
        /// <param name="keyword">关键字：镜像名/端口/容器名</param>
        /// <returns>["CONTAINER ID, NAMES, IMAGE, PORT",......]</returns>
        private static List<string> GetDocker(string dockerParams, string keyword)
        {
            string result = StartProcess("docker", dockerParams, true);//终端输出
            if (string.IsNullOrWhiteSpace(result)) return default;
            Info($">------------docker {dockerParams} BEGIN------------");
            Info($">{result}");
            Info($">------------docker {dockerParams} END------------");
            string[] items = result.Split('\n', '\r', '\t');
            var dockers = new List<string>();
            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item) || !item.Contains(keyword)) continue;
                //CONTAINER ID   IMAGE                            COMMAND                    CREATED          STATUS          PORTS                                             NAMES
                //a9f5f7666981   algorithmcallpy                  "python3 AlgorithmGr…"    32 minutes ago   Up 32 minutes   80 / tcp, 443 / tcp, 0.0.0.0:7802->7800 / tcp     AlgorithmGrpcCall2
                string[] vals = item.Split(' ');
                var imageName = "";
                for (int i = 1; i < vals.Length; i++)
                {
                    imageName = vals[i];
                    if (!string.IsNullOrWhiteSpace(imageName)) break;
                }
                var port = "";
                foreach (var val in vals)
                {
                    if (val.Contains(keyword))
                    {
                        port = val.Substring(val.IndexOf(keyword) - 4, 4);
                        break;
                    }
                }
                dockers.Add($"{vals[0]},{vals[^1]},{imageName},{port}");
            }
            return dockers;
        }
        #endregion

        #region Update Daemon
        /// <summary>
        /// 守护进程更新
        /// </summary>
        private static void DaemonUpdate(string serviceName, string zipFile, string updatePath, UpdateMode mode, int reTry = 1)
        {
            if (reTry < 1) return;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                var oldPid = GetPidFromPs($"{serviceName}.dll");
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->原进程pid[{oldPid}]");
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->解压Zip文件中...");
                ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);

                if (mode == UpdateMode.Cold || mode == UpdateMode.Cold2)
                {
                    #region 关闭原进程+启动新进程
                    //string oneCmd = $"cd {updatePath} && nohup dotnet {serviceName}.dll &";
                    //if (!string.IsNullOrWhiteSpace(oldPid))
                    //{
                    //    oneCmd = $"kill -15 {oldPid} && {oneCmd}";
                    //}
                    //Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->关闭原进程+启动新进程：{oneCmd}");
                    //StartProcess(oneCmd, string.Empty, false);
                    #endregion
                    if (!string.IsNullOrWhiteSpace(oldPid))
                    {
                        Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->关闭原进程[{oldPid}]...");
                        StartProcess("kill", "-15 " + oldPid, false);
                    }
                    string dotnetArg = Path.Combine(updatePath, $"{serviceName}.dll &");
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->启动新进程：dotnet {dotnetArg}");
                    StartProcess("dotnet", dotnetArg, false);
                }
                else
                {
                    string dotnetArg = Path.Combine(updatePath, $"{serviceName}.dll &");
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->启动新进程：dotnet {dotnetArg}");
                    StartProcess("dotnet", dotnetArg, false);
                    if (!string.IsNullOrWhiteSpace(oldPid))
                    {
                        Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->关闭原进程[{oldPid}]...");
                        StartProcess("kill", "-15 " + oldPid, false);
                    }
                }
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->任务完成");
            }
            catch (Exception ex)
            {
                WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->[{nameof(DaemonUpdate)}]{ex}");
                DaemonUpdate(serviceName, zipFile, updatePath, mode, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{serviceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        #endregion

        #region Update Web
        /// <summary>
        /// Update Web
        /// </summary>
        private static void WebUpdate(UpdateServiceConf service, string zipFile, string updatePath, UpdateMode mode, int retryNum = 0)
        {
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                var pid = GetPidFromPs($"{service.ServiceName}.dll");
                var portOld = GetPortByPid(pid);
                if (mode == UpdateMode.Cold || mode == UpdateMode.Cold2)
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->关闭旧应用程序[pid:{pid},port:{portOld}]中...");
                    StartProcess("kill", $"-15 {pid}", false);
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->[{retryNum}]解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
                    StartByNewPort(portOld, service.ServiceName, updatePath);
                }
                else
                {
                    _ = int.TryParse(Configuration[$"{service.ServiceName}:Port"], out var portBase);
                    var portNew = portOld == portBase ? portBase + 100 : portBase;//判断当前占用端口，自动更换端口
                    if (retryNum > 0)
                    {
                        Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->更新执行：重命名中...");
                        RenameTargetFile(zipFile, updatePath, service.ServiceName);
                    }
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->[{retryNum}]解压Zip文件中...");
                    ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);

                    StartByNewPort(portNew, service.ServiceName, updatePath);
                    if (!ReloadNginxConf(portOld, portNew, service))
                    {
                        Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->升级中止");
                        return;
                    }
                }
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->升级完成");

                Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->等待原应用程序处理完旧逻辑...");
                Thread.Sleep(3000);
                KillByPort(portOld, service.ServiceName);
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->旧进程处理完成");
            }
            catch (Exception ex)
            {
                if (retryNum == 0 && ex.Message.Contains("used by another"))
                {
                    WebUpdate(service, zipFile, updatePath, mode, retryNum + 1);
                }
                else
                {
                    WriteRed($"[{DateTime.Now:HH:mm:ss.fff}]{service.ServiceName}->[{nameof(WebUpdate)}]{ex}");
                }
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{service.ServiceName}]耗时:{stopwatch.ElapsedMilliseconds}ms");
                DelTmpFile(updatePath);
            }
        }
        private static void KillByPort(int port, string serviceName)
        {
            if (port < 1) return;
            var dotnetPid = GetPidFromLsofOutput(StartProcess("lsof", "-i:" + port, true), "dotnet");
            if (!string.IsNullOrWhiteSpace(dotnetPid))
            {
                Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->关闭旧应用程序[pid:{dotnetPid},port:{port}]中...");
                StartProcess("kill", $"-15 {dotnetPid}", false);
            }
        }
        /// <summary>
        /// 使用新端口启动程序
        /// </summary>
        private static void StartByNewPort(int port, string serviceName, string updatePath)
        {
            if (port > 100)
            {
                var dotnetPid = GetPidFromLsofOutput(StartProcess("lsof", "-i:" + port, true), "dotnet");
                if (!string.IsNullOrWhiteSpace(dotnetPid))
                {
                    Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->端口{port}已启动该程序.");
                    return;
                }
            }
            string dotnetArg = $"{serviceName}.dll --urls=https://*:{port} &";
            dotnetArg = Path.Combine(updatePath, dotnetArg);
            Info($"[{DateTime.Now:HH:mm:ss.fff}]{serviceName}->用新端口({port})启动程序：dotnet {dotnetArg}");
            StartProcess("dotnet", dotnetArg, false);
        }
        
        /// <summary>
        /// 获取指定pid所占端口
        /// </summary>
        private static int GetPortByPid(string pid)
        {
            if (string.IsNullOrWhiteSpace(pid)) return 0;
            var result = StartProcess("netstat", $"-nap | grep {pid}", true);
            if (string.IsNullOrWhiteSpace(result)) return 0;
            string line = result.Split('\n').Where(r => r.ToLower().Contains(pid + "/dotnet") && r.ToLower().Contains("listen") && r.Contains(":::")).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line)) return 0;

            string[] items = line.Split('\t', '\r', ' ').Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
            var portPart = items[3];
            _ = int.TryParse(portPart.Split(":")[^1], out var port);
            return port;
        }
        #endregion

        #region 获取进程id
        /// <summary>
        /// 从ps -x结果按命令取出pid
        /// </summary>
        /// <param name="processCmd">命令关键字</param>
        /// <returns>进程id</returns>
        private static string GetPidFromPs(string processCmd)
        {
            string pid = string.Empty;
            var result = StartProcess("ps", $"-x", true);
            if (!string.IsNullOrWhiteSpace(result))
            {
                string line = result.Split('\n').Where(r => r.ToLower().Contains(processCmd.ToLower())).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string[] items = line.Split('\t', '\r', ' ').Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                    pid = items[0];
                }
            }
            return pid;
        }
        /// <summary>
        /// 从lsof -i:port结果取出pid
        /// </summary>
        /// <param name="result">lsof -i:port结果</param>
        /// <param name="keyword">过滤关键字</param>
        /// <returns>进程id</returns>
        private static string GetPidFromLsofOutput(string result, string keyword = "dotnet")
        {
            if (string.IsNullOrWhiteSpace(result)) return string.Empty;
            string[] items = result.Split(' ', '\n', '\t', '\r');
            bool chkPID = false;
            string pid = string.Empty;
            foreach (string item in items)
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (item.Contains(keyword)) { chkPID = true; }
                else if (chkPID)
                {
                    pid = item.Trim();
                    break;
                }
            }
            return pid;
        }
        #endregion
    }
}
