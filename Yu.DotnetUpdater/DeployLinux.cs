using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Yu.DotnetUpdater
{
    /// <summary>
    /// 部署至Linux/Nginx
    /// </summary>
    /// <remarks>
    /// <br/>kill 2:  SIGNIT  (Ctrl+C)
    /// <br/>kill 3:  SIGQUIT （退出）
    /// <br/>kill 9:  SIGKILL(强制终止）【不能让程序捕获到】
    /// <br/>kill 15: SIGTERM （终止）
    /// </remarks>
    [System.Runtime.Versioning.SupportedOSPlatform("linux")]
    //[System.Runtime.Versioning.SupportedOSPlatform("maccatalyst")]
    //[System.Runtime.Versioning.SupportedOSPlatform("tvos")]
    //[System.Runtime.Versioning.SupportedOSPlatform("ios")]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public class DeployLinux : Util
    {
        private static string SystemdPath => "/usr/lib/systemd/system";
        #region StartForLinux
        public static void Start(int[] updateIndexs, UpdateServiceConf[] services)
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
                Info($"{services[i].UpdatePack}->查找待更新文件...");
                string zipFile = Path.Combine(ToolRunPath, services[i].UpdatePack);
                if (!File.Exists(zipFile))
                {
                    Info($"{services[i].UpdatePack}->未找到待更新压缩包 : {zipFile}");
                    continue;
                }
                #endregion
                var updatePath = Path.Combine(deployPath, services[i].Path);
                CreateFolder(updatePath);
                #region 备份原文件
                if (UpdateServiceConf.BakDirectoryNameDemo.Contains(services[i].BakDirectoryFormat))
                {
                    string bakPathBase = Path.Combine(deployPath, "bak");
                    string pathBak = Path.Combine(bakPathBase, DateTime.Now.ToString(services[i].BakDirectoryFormat));
                    Info($"{services[i].ServiceName}->备份原文件...");
                    CopyFolderFile(updatePath, pathBak, true, "logs", "log");
                    StartProcess("mkdir", $"{pathBak} -p", true);
                    StartProcess("cp", $"-r {updatePath} {pathBak}", true);

                    Info($"{services[i].ServiceName}->....................................");
                    DeleteOldBak(bakPathBase);
                    Info($"{services[i].ServiceName}->备份原文件完成.");
                }
                #endregion
                var updateMode = (UpdateMode)services[i].UpdateMode;
                Info($"更新模式：{updateMode}");
                UpdateService(updateMode, services[i], zipFile, deployPath, updatePath);
                stopwatch.Stop();
                Info($"[{services[i].UpdatePack}]更新耗时:{stopwatch.ElapsedMilliseconds}ms");
                Thread.Sleep(1000);
            }
        }
        #endregion

        #region 服务更新
        /// <summary>
        /// 服务更新
        /// </summary>
        /// <remarks>
        /// <br/>冷更新：直接按名称查找实例进程id，关闭旧实例进程+启动新实例进程；
        /// <br/>热更新：判断新旧实例，启动新实例进程+关闭旧实例进程；
        /// <br/>--------默认更新启用旧实例；
        /// <br/>--------使用nginx则按端口查进程id，通过[nginx主备代理]判断；
        /// <br/>--------不用nginx则按名称查进程id，通过[运行状态+运行时长]判断；
        /// </remarks>
        private static void UpdateService(UpdateMode mode, UpdateServiceConf service, string zipFile, string deployPath, string updatePath, int reTry = 1)
        {
            if (reTry < 1) return;
            var stopwatch = new Stopwatch();
            try
            {
                stopwatch.Start();
                
                if (mode == UpdateMode.Cold)
                {
                    ColdUpdate(service, zipFile, updatePath);
                }
                else
                {
                    HotUpdate(mode, service, zipFile, updatePath);
                }
                Info($"{service.ServiceName}->更新完成");
                #region 设置开机启动
                if (!string.IsNullOrWhiteSpace(service.SystemdService))
                {
                    var systemdServiceFile = Path.Combine(SystemdPath, service.SystemdService);
                    if (File.Exists(systemdServiceFile))
                    {
                        UpdateBootstrap(service, systemdServiceFile, true);
                    }
                    else
                    {
                        var systemdServiceFile2 = Path.Combine(updatePath, service.ServiceName, service.SystemdService);
                        Info($"->开机启动文件[{systemdServiceFile2}]...");
                        if (File.Exists(systemdServiceFile2))
                        {
                            File.Copy(systemdServiceFile2, systemdServiceFile, true);
                            UpdateBootstrap(service, systemdServiceFile, true);
                        }
                        else
                        {
                            WriteYellow($"{service.ServiceName}->开机启动文件不存在：{systemdServiceFile}");
                        }
                    }
                }
                #endregion
            }
            catch (Exception ex)
            {
                WriteRed($"{service.ServiceName}->[{nameof(UpdateService)}]", ex);
                UpdateService(mode, service, zipFile, deployPath, updatePath, reTry - 1);
            }
            finally
            {
                stopwatch.Stop();
                Info($"[{service.ServiceName}]本次更新耗时:{stopwatch.ElapsedMilliseconds}ms");
            }
        }
        #endregion

        #region 冷更新：关闭旧实例进程+启动新实例进程
        private static void ColdUpdate(UpdateServiceConf service, string zipFile, string updatePath)
        {
            Info($"->解压Zip文件中[{updatePath}]...");
            ZipFile.ExtractToDirectory(zipFile, updatePath, Encoding.UTF8, true);
            var oldPid = GetPidsFromPs($"{service.ServiceName}.dll").FirstOrDefault();//冷更新直接按名称查找
            #region 一次性执行
            //var oneCmd = $"cd {updatePath} && nohup dotnet {BuildArgs(updatePath, service.ServiceName, service.Ports)}";
            //if (!string.IsNullOrWhiteSpace(oldPid))
            //{
            //    oneCmd = $"kill -15 {oldPid} && {oneCmd} && kill -9 {oldPid}";
            //}
            //Info($"{serviceName}->关闭原进程+启动新进程：{oneCmd}");
            //StartProcess(oneCmd, string.Empty, false);
            #endregion
            KillProcess(service.ServiceName, oldPid, 1);
            var dotnetArg = BuildArgs(updatePath, service.ServiceName, service.Ports);
            Info($"{service.ServiceName}->启动新进程：dotnet {dotnetArg}");
            StartProcess("dotnet", dotnetArg, false);
        }
        #endregion

        #region 热更新：启动新实例进程+关闭旧实例进程
        private static void HotUpdate(UpdateMode mode, UpdateServiceConf service, string zipFile, string updatePath)
        {
            string curPid = string.Empty;
            var curPath = updatePath;
            var curPorts = service.Ports;
            string newPid = string.Empty;
            var newPath = updatePath + (mode == UpdateMode.Hot2 ? "2" : null);
            var newPorts = service.BakPorts;
            //有端口：根据端口判断新旧，根据端口确定主备，主备（若有）文件都更新
            //无端口：根据id可判断新旧，单目录模式无法确定主备，主备（若有）文件都更新
            if (!string.IsNullOrWhiteSpace(service.NginxConf))
            {
                #region 判断主备实例-有端口，通过[nginx主备代理]
                //进程的nginx配置提前配好
                string oldNginxConf = Path.Combine(Configuration["NginxConfPath"] + string.Empty, service.NginxConf);
                var text = File.ReadAllText(oldNginxConf, Encoding.UTF8);
                var bakKeys = service.BakPorts.Select(p => $":{p} backup;").ToList();
                if (bakKeys.Any(k => text.Contains(k)))
                {
                    foreach (var port in service.Ports)
                    {
                        curPid = GetPidByPort(port);
                        if (!string.IsNullOrWhiteSpace(curPid)) break;
                    }
                    curPorts = service.Ports;
                    foreach (var port in service.BakPorts)
                    {
                        newPid = GetPidByPort(port);
                        if (!string.IsNullOrWhiteSpace(newPid)) break;
                    }
                    newPorts = service.BakPorts;
                    Info($"->当前为主实例，启用备用实例[{string.Join(",", curPorts)}]->[{string.Join(",", newPorts)}]...");
                }
                else
                {
                    foreach (var port in service.BakPorts)
                    {
                        curPid = GetPidByPort(port);
                        if (!string.IsNullOrWhiteSpace(curPid)) break;
                    }
                    curPorts = service.BakPorts;
                    curPath = updatePath + (mode == UpdateMode.Hot2 ? "2" : null);
                    foreach (var port in service.Ports)
                    {
                        newPid = GetPidByPort(port);
                        if (!string.IsNullOrWhiteSpace(newPid)) break;
                    }
                    newPath = updatePath;
                    newPorts = service.Ports;
                    Info($"->当前为备用实例，启用主实例[{string.Join(",", curPorts)},{curPath}]->[{string.Join(",", newPorts)},{newPath}]...");
                }
                #region MyRegion
                //var mainKeys = service.Ports.Select(p => $":{p} backup;").ToList();
                //foreach (var line in text.ToString().Split("\n"))
                //{
                //    if (bakKeys.Any(k => text.Contains(k)))
                //    {
                //        curPid = GetPidByPort(service.Ports[0]);
                //        curPorts = service.Ports;
                //        curPath = updatePath + (mode == UpdateMode.Hot2 ? "2" : null);
                //        newPid = GetPidByPort(service.BakPorts[0]);
                //        newPath = updatePath;
                //        newPorts = service.BakPorts;
                //        Info($"->当前为主实例，启用备用实例[{string.Join(",", curPorts)}]->[{string.Join(",", newPorts)}]...");
                //        break;
                //    }
                //    else if (mainKeys.Any(k => text.Contains(k)))
                //    {
                //        curPid = GetPidByPort(service.BakPorts[0]);
                //        curPorts = service.BakPorts;
                //        newPid = GetPidByPort(service.Ports[0]);
                //        newPorts = service.Ports;
                //        Info($"->nginx-当前为备用实例，启用主实例[{string.Join(",", curPorts)}]->[{string.Join(",", newPorts)}]...");
                //        break;
                //    }
                //}
                #endregion
                #endregion
            }
            else
            {
                #region 判断主备实例-无端口或有端口无代理：按名称查找，通过[运行状态+运行时长]判断
                var pids = GetPidsFromPs($"{service.ServiceName}.dll");
                Info($"->当前实例ids[{string.Join(",", pids)}]...");
                if (pids.Count > 1)
                {
                    var firstPid = pids.FirstOrDefault()!;
                    var lastPid = pids.LastOrDefault()!;
                    var firstSeconds = ElapsedSeconds(firstPid);
                    var lastSeconds = ElapsedSeconds(lastPid);
                    if (firstSeconds < lastSeconds)
                    {
                        curPid = firstPid;
                        curPath = ProcessFileDirectory(firstPid, service.ServiceName);
                        newPid = lastPid;
                        if (mode == UpdateMode.Hot2)
                        {
                            newPath = GetNewPath(curPath);
                        }
                        else
                        {
                            newPath = ProcessFileDirectory(firstPid, service.ServiceName);
                        }
                        Info($"->实例切换[{curPid},{curPath}]-->[{newPid},{newPath}]...");
                    }
                    else
                    {
                        curPid = lastPid;
                        curPath = ProcessFileDirectory(lastPid, service.ServiceName);
                        newPid = firstPid;
                        if (mode == UpdateMode.Hot2)
                        {
                            newPath = GetNewPath(curPath);
                        }
                        else
                        {
                            newPath = ProcessFileDirectory(firstPid, service.ServiceName);
                        }
                        Info($"->实例切换[{curPid},{curPath}]-->[{newPid},{newPath}]...");
                    }
                }
                else if (pids.Count == 1)
                {
                    curPid = pids.FirstOrDefault()!;
                    curPath = ProcessFileDirectory(curPid, service.ServiceName);
                    newPath = GetNewPath(curPath);
                    Info($"->当前实例[{curPid},{curPath}]...");
                }
                #endregion
            }
            if (!string.IsNullOrWhiteSpace(newPid))
            {
                Info($"->关掉待更新进程[{newPid}]...");
                StartProcess("kill", $"-9 {newPid}", false);
            }
            if (string.IsNullOrWhiteSpace(curPath))
            {
                WriteYellow($"->当前实例文件路径为空！");
                return;
            }
            if (string.IsNullOrWhiteSpace(newPath))
            {
                WriteYellow($"->待更新路径为空！");
                return;
            }
            Info($"->解压Zip文件中-旧[{curPath}]...");//双目录模式：主备实例文件都更新，避免开机自启后有旧逻辑影响
            ZipFile.ExtractToDirectory(zipFile, curPath, Encoding.UTF8, true);
            if (mode == UpdateMode.Hot2)
            {
                CreateFolder(newPath);
                Info($"->解压Zip文件中-新[{newPid},{newPath}]...");
                ZipFile.ExtractToDirectory(zipFile, newPath, Encoding.UTF8, true);
                if (service.KillOldWaitSeconds > 0)
                {
                    if (string.IsNullOrWhiteSpace(service.SystemdService))
                    {
                        var serviceFile = service.SystemdService;
                        if (newPath.EndsWith("2")) serviceFile = $"{service.ServiceName.ToLower()}2.service";
                        var systemdServiceFile = Path.Combine(SystemdPath, serviceFile);
                        UpdateBootstrap(service, systemdServiceFile, false);//双目录模式：禁止开机自启，避免启动后有逻辑影响
                    }
                }
            }
            var dotnetArg = BuildArgs(newPath, service.ServiceName, newPorts);
            Info($"{service.ServiceName}->启动新进程：dotnet {dotnetArg}");
            StartProcess("dotnet", dotnetArg, false);
            if (!string.IsNullOrWhiteSpace(service.NginxConf))
            {
                Thread.Sleep(5000);
                ReloadNginxConf(curPorts, newPorts, service);
            }
            #region 停止服务+禁止自启
            if (service.KillOldWaitSeconds > 0)
            {
                if (mode == UpdateMode.Hot2)
                {
                    if (string.IsNullOrWhiteSpace(service.SystemdService))
                    {
                        var serviceFile = service.SystemdService;
                        if (newPath.EndsWith("2")) serviceFile = $"{service.ServiceName.ToLower()}2.service";
                        var systemdServiceFile = Path.Combine(SystemdPath, serviceFile);
                        UpdateBootstrap(service, systemdServiceFile, false);//双目录模式：禁止开机自启，避免启动后有逻辑影响
                    }
                }
                new Thread(delegate ()
                {
                    KillProcess(curPid, service.ServiceName, service.KillOldWaitSeconds);
                })
                { IsBackground = true }.Start();
            }
            #endregion
        }
        private static string GetNewPath(string curPath)
        {
            string newPath;
            if (curPath.EndsWith("2"))
            {
                newPath = curPath.Replace("2", null);
            }
            else
            {
                newPath = curPath + "2";
            }
            return newPath;
        }
        #endregion

        #region 设置开机启动
        private static void UpdateBootstrap(UpdateServiceConf service, string systemdServiceFile, bool enable)
        {
            if (!enable && !File.Exists(systemdServiceFile))
            {
                Info($"{service.ServiceName}->开机启动文件不存在：{systemdServiceFile}");
                return;
            }
            Info($"->{(enable ? "启用" : "禁止")}开机启动[{systemdServiceFile}]...");
            StartProcess("systemctl", $"{(enable ? "enable" : "disable")} {service.SystemdService}", true);
            Info($"{service.ServiceName}->{(enable ? "启用" : "禁止")}开机启动完成");
        }
        #endregion

        #region KillProcess
        private static void KillProcess(string pid, string serviceName, int killWaitSeconds = 1)
        {
            if (killWaitSeconds > 0 && !string.IsNullOrWhiteSpace(pid))
            {
                Thread.Sleep(killWaitSeconds * 1000);
                Info($"{serviceName}->关闭进程[{pid}]...");
                StartProcess("kill", $"-15 {pid}", false);
                Thread.Sleep(3000);
                StartProcess("kill", $"-9 {pid}", false);
            }
        }
        #endregion
        #region BuildArgs
        private static string BuildArgs(string updatePath, string serviceName, params int[] ports)
        {
            var dotnetArg = Path.Combine(updatePath, $"{serviceName}.dll");
            if (ports != null)
            {
                for (var p = 0; p < ports.Length; p++)
                {
                    var port = ports[p];
                    if (port > 0)
                    {
                        if (p == 0)
                        {
                            dotnetArg += $" --urls=https://*:{port}";
                        }
                        else
                        {
                            dotnetArg += $" --p{p}={port}";
                        }
                    }
                }
            }
            dotnetArg += $" &";
            return dotnetArg;
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

        #region 获取进程id：从 ps -x 输出结果按关键字取
        /// <summary>
        /// 获取进程id：从 ps -x 输出结果按关键字取出pid
        /// </summary>
        /// <param name="keyword">关键字</param>
        /// <returns>进程id</returns>
        private static List<string> GetPidsFromPs(string keyword)
        {
            var pids = new List<string>();
            var result = StartProcess("ps", $"-x", true);
            if (!string.IsNullOrWhiteSpace(result))
            {
                var lines = result.Split('\n').Where(r => r.ToLower().Contains(keyword.ToLower())).ToList();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        string[] items = line.Split('\t', '\r', ' ').Where(a => !string.IsNullOrWhiteSpace(a)).ToArray();
                        pids.Add(items[0]);
                    }
                }
            }
            return pids;
        }
        #endregion

        #region 获取进程id：从 lsof -i:port 输出结果按关键字取
        /// <summary>
        /// 获取进程id：从 lsof -i:port 输出结果按关键字取
        /// </summary>
        /// <param name="port">端口</param>
        /// <param name="keyword">过滤关键字</param>
        /// <returns>进程id</returns>
        private static string GetPidByPort(int port, string keyword = "dotnet")
        {
            var result = StartProcess("lsof", "-i:" + port, true);
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

        #region 获取指定pid所占端口
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

        #region 进程的文件目录
        /// <summary>
        /// 进程的文件目录：ps -ax|grep keyword，ps -ef|grep keyword
        /// </summary>
        private static string ProcessFileDirectory(string key1, string key2)
        {
            //var result = StartProcess("ps", "-ax", true);//ps -ax|grep keyword
            var result = StartProcess("/bin/bash", $"-c \"ps -ax|grep {key1}\"", true);
            //1932 ?        Sl     0:01 dotnet /home/Deploy/ProjectName/ProjectName.dll --urls=https://*:5000 &
            string[] keywords = new string[] { key1.ToLower(), key2.ToLower() };
            var path = GetFileDirectory(result, keywords);
            if (string.IsNullOrWhiteSpace(path))
            {
                //result = StartProcess("ps", "-ef", true);//ps -ef|grep keyword
                result = StartProcess("/bin/bash", $"-c \"ps -ef|grep {key1}\"", true);
                //root        1932       1  0 09:34 ?        00:00:01 dotnet /home/Deploy/ProjectName/ProjectName.dll --urls=https://*:5000 &
                path = GetFileDirectory(result, keywords);
            }
            Info($"ProcessFileDirectory[{key1},{key2}][{path}]");
            return path!;
        }
        private static string GetFileDirectory(string result, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(result)) return string.Empty;
            Info($"[GetFileDirectory]-->[{result}]");
            var line = result.Split('\n').Where(t => keywords.All(t.ToLower().Contains)).FirstOrDefault() + string.Empty;
            Info($"[GetFileDirectory][{line}]");
            var path = line.Split('\t', '\r', ' ').Where(a => a.ToLower().Contains(keywords[1])).FirstOrDefault();
            path = Path.GetDirectoryName(path) + string.Empty;
            return path;
        }
        #endregion
        #region 进程启动时的工作目录
        /// <summary>
        /// 进程启动时的工作目录：pwdx 123
        /// </summary>
        private static string ProcessWorkDirectory(string pid)
        {
            var result = StartProcess("pwdx", $"{pid}", true);
            //4162: /home/Deploy/ProjectName
            result = result.Replace($"{pid}:", null).Trim();
            if (string.IsNullOrWhiteSpace(result))
            {
                result = StartProcess("lsof", $"-p {pid} | grep cwd", true);
                //dotnet  4162 root  cwd       DIR              253,0     4096 295992 /home//Deploy/ProjectName
                result = result.Substring(result.IndexOf("/"));
            }
            return result;
        }
        #endregion

        #region 进程启动时间/运行时间/运行时长
        private static TimeSpan ElapsedTimes(object pid)
        {
            var result = StartProcess("ps", $"-p {pid} -o etime=", true);
            if (string.IsNullOrWhiteSpace(result)) return default;
            _ = TimeSpan.TryParse(result, out var time);
            return time;
        }
        private static long ElapsedSeconds(object pid)
        {
            var result = StartProcess("ps", $"-p {pid} -o etimes=", true);
            _ = long.TryParse(result, out var seconds);
            return seconds;
        }
        private static DateTime StartTime(object pid)
        {
            var result = StartProcess("ps", $" -p {pid} -o lstart=", true);
            if (string.IsNullOrWhiteSpace(result)) return default;
            _ = DateTime.TryParse(result, out var time);
            return time;
        }
        #endregion
    }
}
