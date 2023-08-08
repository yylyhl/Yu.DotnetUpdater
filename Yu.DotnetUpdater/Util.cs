using Microsoft.Extensions.Configuration;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Yu.DotnetUpdater
{
    public class Util
    {
        #region 有错误或警告
        /// <summary>
        /// 有错误或警告
        /// </summary>
        static bool _warnOrError = false;
        /// <summary>
        /// 有错误或警告
        /// </summary>
        public static bool WarnOrError
        {
            get { return _warnOrError; }
            set { _warnOrError = value; }
        }
        #endregion

        /// <summary>
        /// 直接读取配置 .Configuration["一级节点名:二级节点名"];
        /// </summary>
        public static IConfiguration Configuration { get; set; }

        /// <summary>
        /// 更新工具所在目录
        /// </summary>
        public static string ToolRunPath => AppDomain.CurrentDomain.BaseDirectory;

        #region Console.WriteLine
        /// <summary>
        /// Console.WriteLine(msg);Default
        /// </summary>
        public static void Info(string msg)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}]{msg}");
        }
        /// <summary>
        /// Console.WriteLine(msg);Green
        /// </summary>
        public static void WriteGreen(string msg)
        {
            var defColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Green;
            Info(msg);
            Console.ForegroundColor = defColor;
        }
        /// <summary>
        /// Console.WriteLine(msg);Yellow
        /// </summary>
        public static void WriteYellow(string msg)
        {
            WarnOrError = true;
            var defColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Info(msg);
            Console.ForegroundColor = defColor;
        }
        /// <summary>
        /// Console.WriteLine(msg);Red
        /// </summary>
        public static void WriteRed(string msg, Exception? ex = null)
        {
            WarnOrError = true;
            var defColor = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.Red;
            Info(msg);
            Console.WriteLine(ex);
            Console.ForegroundColor = defColor;
        }
        #endregion

        #region 文件备份目录
        /// <summary>
        /// 文件备份目录
        /// </summary>
        public static string BakPathBase(string osPath)
        {
            return Path.Combine(osPath, "bak", DateTime.Now.ToString("yyyyMMddHHmm"));
        }
        #endregion

        #region 获取压缩包里编译文件名
        /// <summary>
        /// 获取压缩包里编译文件名
        /// </summary>
        static List<string> GetZipNames(string zipFile)
        {
            List<string> fileNames = new List<string>();
            using (var zipArchive = ZipFile.Open(zipFile, ZipArchiveMode.Read, Encoding.UTF8))
            {
                foreach (var entrty in zipArchive.Entries)
                {
                    //if (string.IsNullOrWhiteSpace(entrty.Name) || entrty.FullName.StartsWith("runtimes")) continue;
                    if (entrty.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || entrty.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || entrty.Name.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
                    {
                        fileNames.Add(entrty.FullName);
                    }
                }
            }
            return fileNames;
        }
        #endregion

        #region 重命名目标文件
        /// <summary>
        /// 重命名目标文件
        /// </summary>
        public static void RenameTargetFile(string zipFile, string updatePath, string serviceName)
        {
            Info($"{updatePath}->清理 *.temp 文件...");
            DelTmpFile(updatePath);
            Info($"{serviceName}->重命名目标文件...");
            List<string> dllNames = GetZipNames(zipFile);
            if (dllNames.Count == 0) return;
            foreach (string dllName in dllNames)
            {
                string dllFile = Path.Combine(updatePath, dllName);
                if (!File.Exists(dllFile)) continue;
                string temp = dllName + ".temp";
                try
                {
                    File.Move(dllFile, Path.Combine(updatePath, temp), true);
                }
                catch (Exception ex)
                {
                    WriteYellow($"{nameof(RenameTargetFile)}[{dllFile}]{ex.Message}");
                }
            }
            Thread.Sleep(3000);
        }
        #endregion

        #region 删除临时文件
        /// <summary>
        /// 删除临时文件
        /// </summary>
        public static void DelTmpFile(string updatePath, int waitSeconds = 0, bool outputLog = true)
        {
            if (waitSeconds > 0) Thread.Sleep(waitSeconds * 1000);
            string[] files = Directory.GetFiles(updatePath, "*.temp");
            foreach (var file in files)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    WriteYellow($"{nameof(DelTmpFile)}[{file}]{ex.Message}");
                }
            }
            foreach (var folder in Directory.GetDirectories(updatePath))
            {
                DelTmpFile(folder, 0);
            }
        }
        #endregion

        #region 删除旧备份
        /// <summary>
        /// 删除最近三次之前的旧备份
        /// </summary>
        /// <remarks>针对路径：serviceBak/serviceName</remarks>
        public static void DeleteOldBak(string serviceBak)
        {
            if (!Directory.Exists(serviceBak)) return;
            Info($"删除旧备份[{serviceBak}]");
            var folders = Directory.GetDirectories(serviceBak).OrderBy(f => f).ToList();
            if (folders.Count <= 3) return;

            var dels = folders.Take(folders.Count - 3).ToList();
            foreach (string path in dels)
            {
                Directory.Delete(path, true);
            }
        }
        #endregion

        #region 复制文件夹及文件 不含根目录
        /// <summary>
        /// 复制文件夹及文件 不含根目录
        /// </summary>
        /// <param name="sourceFolder">原文件路径</param>
        /// <param name="destFolder">目标文件路径</param>
        public static void CopyChildFolderFile(string sourceFolder, string destFolder, bool overwrite, params string[] exceptFolders)
        {
            try
            {
                if (!Directory.Exists(sourceFolder)) return;
                CreateFolder(destFolder);
                //得到原文件根目录下的所有文件
                string[] files = Directory.GetFiles(sourceFolder);
                foreach (string file in files)
                {
                    string name = Path.GetFileName(file);
                    string dest = Path.Combine(destFolder, name);
                    File.Copy(file, dest, overwrite);//复制文件
                }
                //得到原文件根目录下的所有文件夹
                string[] folders = Directory.GetDirectories(sourceFolder);
                foreach (string folder in folders)
                {
                    string name = Path.GetFileName(folder);
                    //if (string.Compare(name, "logs", true) == 0 || string.Compare(name, "log", true) == 0) continue;//过滤日志
                    if (exceptFolders != null && exceptFolders.Any(fname => string.Compare(name, fname, true) == 0)) continue;
                    string dest = Path.Combine(destFolder, name);
                    CopyChildFolderFile(folder, dest, overwrite, exceptFolders);//构建目标路径,递归复制文件
                }
            }
            catch (Exception ex)
            {
                WriteRed($"[{nameof(CopyChildFolderFile)}]{ex.Message}");
            }
        }
        #endregion

        #region 复制文件夹及文件 含根目录
        /// <summary>
        /// 复制文件夹及文件 含根目录
        /// </summary>
        /// <param name="sourceFolder">原文件路径</param>
        /// <param name="destFolder">目标文件路径</param>
        /// <param name="exceptFolders">除外目录</param>
        public static void CopyFolderFile(string sourceFolder, string destFolder, bool overwrite, params string[] exceptFolders)
        {
            try
            {
                if (!Directory.Exists(sourceFolder)) return;
                CreateFolder(destFolder);

                string folderName = Path.GetFileName(sourceFolder);
                string destfolderdir = Path.Combine(destFolder, folderName);
                string[] filenames = Directory.GetFileSystemEntries(sourceFolder);
                foreach (string file in filenames)// 遍历所有的文件和目录
                {
                    string name = Path.GetFileName(file);
                    //if (string.Compare(name, "logs", true) == 0 || string.Compare(name, "log", true) == 0) continue;//过滤日志
                    if (exceptFolders != null && exceptFolders.Any(fname => string.Compare(name, fname, true) == 0)) continue;
                    if (Directory.Exists(file))
                    {
                        string currentdir = Path.Combine(destfolderdir, name);
                        CreateFolder(currentdir);
                        CopyFolderFile(file, destfolderdir, overwrite);
                    }
                    else
                    {
                        string srcfileName = Path.Combine(destfolderdir, name);
                        CreateFolder(destfolderdir);
                        File.Copy(file, srcfileName, overwrite);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteRed($"[{nameof(CopyFolderFile)}]{ex.Message}");
            }
        }
        #endregion

        #region 创建目录(若不存在)
        public static void CreateFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
        }
        #endregion

        #region 更新 nginx/conf/conf.d/site.conf
        /// <summary>
        /// 重载nginx配置 有待更新.conf文件则直接覆盖 无则只改端口
        /// </summary>
        protected static bool ReloadNginxConf(int[] oldPorts, int[] newPorts, UpdateServiceConf service)
        {
            string oldNginxConf = Path.Combine(Configuration["NginxConfPath"] + string.Empty, service.NginxConf);
            string confName = Path.GetFileName(oldNginxConf);
            string newNginxConf = Path.Combine(ToolRunPath, "nginx", confName);
            if (File.Exists(newNginxConf))
            {
                Info($"{service.ServiceName}->Nginx配置转移中...");
                File.Copy(newNginxConf, oldNginxConf, true);
            }
            else
            {
                if (oldPorts.Length < 1 || !oldPorts.Any(p => p > 0) || newPorts.Length < 1 || !newPorts.Any(p => p > 0))
                {
                    WriteYellow($"{service.ServiceName}->端口不正确: [{string.Join(',',oldPorts)}]->[{string.Join(',', newPorts)}]");
                    return false;
                }
                UpdateNginxConf(oldNginxConf, oldPorts, newPorts, service.ServiceName, true);
            }
            try
            {
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    //Info($"{service.ServiceName}->Nginx配置重载中：nginx -s reload...");
                    //StartProcess(Path.Combine(oldNginxConf.Split("conf")[0], "nginx.exe"), "-s reload", false);
                    Info($"{service.ServiceName}->Nginx配置重载中[Windows psexec.exe]...");
                    ////https://www.cnblogs.com/yylyhl/p/17434404.html
                    StartProcess("\"C:\\Program Files\\PSTools\\psexec.exe\"", "-s \"C:\\Program Files\\nginx\\nginx.exe\" -p \"C:\\Program Files\\nginx\" -s reload", false);
                    return true;
                }
                else
                {
                    Info($"{service.ServiceName}->Nginx配置重载中：nginx -s reload...");
                    StartProcess("nginx", "-s reload", false);
                }
            }
            catch (Exception ex)
            {
                WriteRed($"{service.ServiceName}->Nginx配置重载失败", ex);
                return false;
            }
            return true;
        }

        /// <summary>
        /// 更新 nginx/conf/conf.d/site.conf
        /// </summary>
        /// <param name="oldNginxConf">配置文件</param>
        /// <param name="oldPorts">旧端口</param>
        /// <param name="newPorts">新端口</param>
        /// <param name="service">服务</param>
        /// <param name="switchBackup">是否切换主备方式，否则更改端口方式</param>
        protected static void UpdateNginxConf(string oldNginxConf, int[] oldPorts, int[] newPorts, string service, bool switchBackup)
        {
            Info($"{service}->Nginx{(switchBackup ? "主备切换" : "端口修改")}([{string.Join(',', oldPorts)}]->[{string.Join(',', newPorts)}])：{oldNginxConf}");
            var text = File.ReadAllText(oldNginxConf, Encoding.UTF8);
            if (switchBackup)
            {
                for (var p = 0; p < newPorts.Length; p++)
                {
                    var newPort = newPorts[p];
                    var oldPort = oldPorts[p];
                    if (text.Contains($":{newPort} backup;"))
                    {
                        //主备切换-再次
                        text = text.Replace($":{newPort} backup;", $":{newPort};");
                        text = text.Replace($":{oldPort};", $":{oldPort} backup;");
                    }
                    else
                    {
                        //主备切换-初次
                        var host = "server 127.0.0.1";
                        text = text.Replace($"{host}:{oldPort};", $"{host}:{newPort};\r\n{host}:{oldPort} backup;");
                    }
                }
            }
            else
            {
                for (var p = 0; p < newPorts.Length; p++)
                {
                    var newPort = newPorts[p];
                    var oldPort = oldPorts[p];
                    text = text.Replace($":{oldPort}", $":{newPort}");
                }
            }
            //生成不带bom的utf8文件//https://learn.microsoft.com/zh-cn/dotnet/api/system.text.utf8encoding?view=net-6.0
            File.WriteAllText(oldNginxConf, text, new UTF8Encoding(false));
            //File.WriteAllText(oldNginxConf, text, Encoding.UTF8);//生成带bom的utf8文件
            //CmdNonOutput("tail", $"-c +4 {oldNginxConf} > {oldNginxConf}");//转为无bom的utf8文件
        }
        #endregion

        #region 执行命令
        public static string StartProcess(string cmd, string args, bool redirectStandardOutput)
        {
            using Process process = new();
            process.StartInfo.FileName = cmd;
            process.StartInfo.Arguments = args;
            process.StartInfo.RedirectStandardError = redirectStandardOutput;
            process.StartInfo.RedirectStandardOutput = redirectStandardOutput;
            //process.StartInfo.RedirectStandardInput = redirectStandardOutput;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            process.StartInfo.CreateNoWindow = true;
            process.Start();

            //process.StandardInput.WriteLine(cmdargs);
            //process.StandardInput.WriteLine("exit");

            return redirectStandardOutput ? process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd() : string.Empty;
        }
        #endregion

        #region 文件是否被占用：直接打开文件，通过异常判断
        /// <summary>
        /// 文件是否被占用：直接打开文件，通过异常判断
        /// </summary>
        /// <param name="fileFullNmae">文件的完全限定名</param>
        /// <returns>正被占用为true,否则为false </returns>
        protected static bool IsFileInUse(string fileFullName)
        {
            if (!File.Exists(fileFullName)) return false;
            bool inUse = true;
            FileStream fs = null;

        NotComplate: try
            {
                File.OpenRead(fileFullName);
            }
            catch (Exception e)
            {
                Console.WriteLine("文件还未复制完成");
                Thread.Sleep(TimeSpan.FromSeconds(3));
                goto NotComplate;
            }


            try
            {
                //fs = File.Open(fileFullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                fs = new FileStream(fileFullName, FileMode.Open, FileAccess.Read, FileShare.None);
                inUse = false;
            }
            finally
            {
                if (fs != null) fs.Close();
            }
            return inUse;
        }
        #endregion
    }
}
