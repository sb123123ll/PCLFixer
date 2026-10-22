using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PCLFixer
{
    public class FixResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Details { get; set; } = new List<string>();
    }

    public static class PclPatcherEngine
    {
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetFileAttributesW(string lpFileName, uint dwFileAttributes);

        public const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        public const uint FILE_ATTRIBUTE_HIDDEN = 0x02;

        public static void UnlockFile(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    SetFileAttributesW(filePath, FILE_ATTRIBUTE_NORMAL);
                    File.SetAttributes(filePath, FileAttributes.Normal);
                }
                catch { }
            }
        }

        public static void SetHidden(string filePath)
        {
            if (File.Exists(filePath))
            {
                try
                {
                    SetFileAttributesW(filePath, FILE_ATTRIBUTE_HIDDEN);
                }
                catch { }
            }
        }

        public static int KillPclProcesses(Action<string> log)
        {
            int killed = 0;
            int currentPid = Process.GetCurrentProcess().Id;
            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id == currentPid) continue;

                        string name = p.ProcessName.ToLower();
                        if ((name.Contains("plain craft launcher") || name.Contains("pcl")) && !name.Contains("fixer") && !name.Contains("修复"))
                        {
                            log?.Invoke($"发现正在运行的 PCL 进程: {p.ProcessName} (PID: {p.Id})，正在终止...");
                            p.Kill();
                            p.WaitForExit(3000);
                            killed++;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"检查运行中进程时出现提示: {ex.Message}");
            }
            return killed;
        }

        public static void CleanNoticeCaches(Action<string> log)
        {
            try
            {
                string tempDir = Path.GetTempPath();
                string[] files = new string[]
                {
                    Path.Combine(tempDir, @"PCL\Cache\Notice.json"),
                    Path.Combine(tempDir, @"PCL\Cache\Notice.cfg"),
                    Path.Combine(tempDir, @"PCL\Cache\Notice.txt")
                };

                foreach (var f in files)
                {
                    if (File.Exists(f))
                    {
                        try
                        {
                            UnlockFile(f);
                            File.Delete(f);
                            log?.Invoke($"已清理云端下发的停服公告缓存: {f}");
                        }
                        catch (Exception ex)
                        {
                            log?.Invoke($"清理缓存文件 {f} 失败: {ex.Message}");
                        }
                    }
                }
            }
            catch { }
        }

        public static byte[] GetEmbeddedPatchedCache()
        {
            var asm = Assembly.GetExecutingAssembly();
            using (var stream = asm.GetManifestResourceStream("PCLFixer.pcl_cache_payload.gz"))
            {
                if (stream == null)
                    throw new InvalidOperationException("未找到内嵌的修复核心资源包！");
                using (var gz = new GZipStream(stream, CompressionMode.Decompress))
                using (var ms = new MemoryStream())
                {
                    gz.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }

        public static List<string> FindPclLaunchers(Action<string> log)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

            try
            {
                var processes = Process.GetProcesses();
                foreach (var p in processes)
                {
                    try
                    {
                        string fn = p.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(fn) && File.Exists(fn))
                        {
                            if (string.Equals(fn, currentExePath, StringComparison.OrdinalIgnoreCase))
                                continue;

                            string lfn = Path.GetFileName(fn).ToLower();
                            if ((lfn.Contains("plain craft launcher") || lfn.Contains("pcl")) && !lfn.Contains("fixer") && !lfn.Contains("修复"))
                            {
                                found.Add(fn);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            string[] commonRoots = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
                @"D:\",
                @"E:\",
                @"F:\"
            };

            foreach (var root in commonRoots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

                log?.Invoke($"正在扫描常用目录: {root} ...");
                ScanDirectoryQuick(root, found, 0, 3);
            }

            return new List<string>(found);
        }

        private static void ScanDirectoryQuick(string dir, HashSet<string> result, int depth, int maxDepth)
        {
            if (depth > maxDepth) return;
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

            try
            {
                var files = Directory.GetFiles(dir, "*Plain Craft Launcher*.exe", SearchOption.TopDirectoryOnly);
                foreach (var f in files)
                {
                    if (string.Equals(f, currentExePath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    string fn = Path.GetFileName(f).ToLower();
                    if (fn.Contains("fixer") || fn.Contains("修复"))
                        continue;

                    result.Add(f);
                }

                if (depth < maxDepth)
                {
                    var subdirs = Directory.GetDirectories(dir);
                    foreach (var sub in subdirs)
                    {
                        string name = Path.GetFileName(sub).ToLower();
                        if (name == "$recycle.bin" || name == "system volume information" || name == "windows" || name == "node_modules" || name.StartsWith("."))
                            continue;

                        ScanDirectoryQuick(sub, result, depth + 1, maxDepth);
                    }
                }
            }
            catch { }
        }

        public static FixResult FixPclInstallation(string exePath, Action<string> log)
        {
            var res = new FixResult();

            if (!File.Exists(exePath))
            {
                res.Success = false;
                res.Message = "所选文件不存在！";
                return res;
            }

            string dir = Path.GetDirectoryName(exePath);
            string exeName = Path.GetFileName(exePath);
            string cachePath = Path.Combine(dir, "._cache_" + (exeName.StartsWith("._cache_") ? exeName.Substring(8) : exeName));

            log?.Invoke($"========== 开始处理: {exePath} ==========");

            try
            {
                UnlockFile(exePath);
                if (File.Exists(cachePath)) UnlockFile(cachePath);

                byte[] embeddedPayload = GetEmbeddedPatchedCache();

                bool isDirectCache = exeName.StartsWith("._cache_");
                bool isNativeWrapper = false;

                byte[] targetBytes = File.ReadAllBytes(exePath);
                int innerOffset = 0xB46EC;
                int innerLength = 6022144;

                if (targetBytes.Length >= innerOffset + innerLength)
                {
                    if (targetBytes[innerOffset] == 0x4D && targetBytes[innerOffset + 1] == 0x5A)
                    {
                        isNativeWrapper = true;
                    }
                }

                if (isNativeWrapper)
                {
                    log?.Invoke($"检测到当前文件为 Native 壳程序 ({exeName})，内嵌了托管核心。");

                    try
                    {
                        string bak = exePath + ".bak";
                        if (!File.Exists(bak)) File.Copy(exePath, bak, true);
                    }
                    catch { }

                    PatchClrAssemblyInMemory(targetBytes, innerOffset, innerLength, log);

                    File.WriteAllBytes(exePath, targetBytes);
                    log?.Invoke($"成功修补 Native 引导壳中内嵌的托管核心！");

                    log?.Invoke($"正在部署/覆盖热更新隐藏运行库: {cachePath} ...");
                    try
                    {
                        if (File.Exists(cachePath))
                        {
                            UnlockFile(cachePath);
                            File.Delete(cachePath);
                        }
                    }
                    catch { }

                    File.WriteAllBytes(cachePath, embeddedPayload);
                    SetHidden(cachePath);
                    log?.Invoke($"成功部署已解除所有下载限制的最新 2.99.0.0 核心运行库！");
                }
                else
                {
                    log?.Invoke($"检测到当前文件为 .NET 独立程序集/缓存核心 ({exeName})。");

                    try
                    {
                        string bak = exePath + ".bak";
                        if (!File.Exists(bak)) File.Copy(exePath, bak, true);
                    }
                    catch { }

                    bool patchedDirectly = PatchClrAssemblyInMemory(targetBytes, 0, targetBytes.Length, log);
                    if (patchedDirectly)
                    {
                        File.WriteAllBytes(exePath, targetBytes);
                        log?.Invoke($"成功修补文件本体的字节码！");
                    }
                    else
                    {
                        log?.Invoke($"字节码未匹配，直接写入全功能无限制修复核心...");
                        File.WriteAllBytes(exePath, embeddedPayload);
                    }

                    if (!isDirectCache)
                    {
                        log?.Invoke($"同步部署同目录热更新核心: {cachePath} ...");
                        try
                        {
                            if (File.Exists(cachePath))
                            {
                                UnlockFile(cachePath);
                                File.Delete(cachePath);
                            }
                        }
                        catch { }
                        File.WriteAllBytes(cachePath, embeddedPayload);
                        SetHidden(cachePath);
                    }
                }

                CleanNoticeCaches(log);

                res.Success = true;
                res.Message = "修复成功！原版MC下载与版本列表功能已完美恢复。";
                log?.Invoke(res.Message);
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.Message = "修复失败: " + ex.Message;
                log?.Invoke(res.Message);
            }

            return res;
        }

        private static bool PatchClrAssemblyInMemory(byte[] buffer, int offset, int length, Action<string> log)
        {
            bool patchedAny = false;

            try
            {
                byte[] patternLoaderInit = new byte[] {
                    0x14, 0x72, 0x8a, 0x49, 0x01, 0x70, 0x7e, 0xfd, 0x09, 0x00, 0x04
                };
                int idxLoaderInit = IndexOfSequence(buffer, offset, length, patternLoaderInit);
                if (idxLoaderInit != -1)
                {
                    int patchStart = idxLoaderInit + 1; // IL_001a
                    byte[] ldsfldDlClientListLoader = new byte[] { 0x7E, 0x0F, 0x04, 0x00, 0x04 };
                    Buffer.BlockCopy(ldsfldDlClientListLoader, 0, buffer, patchStart, 5);
                    for (int i = 5; i < 48; i++)
                    {
                        buffer[patchStart + i] = 0x00; // nop
                    }
                    log?.Invoke($"[引擎] 已修补 PageDownloadInstall::LoaderInit，恢复原版版本列表加载器！");
                    patchedAny = true;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[引擎] 修补 LoaderInit 提示: {ex.Message}");
            }

            try
            {
                byte[] patternServerSub = new byte[] {
                    0x28, 0xf8, 0x03, 0x00, 0x0a, 0x72, 0xd7, 0xf9, 0x00, 0x70, 0x28, 0xc0, 0x00, 0x00, 0x0a
                };
                int idxServerSub = IndexOfSequence(buffer, offset, length, patternServerSub);
                if (idxServerSub != -1 && idxServerSub >= offset + 12)
                {
                    int headerPos = idxServerSub - 12;
                    buffer[headerPos] = 0x06;
                    buffer[headerPos + 1] = 0x2A;
                    for (int i = 2; i < 12; i++) buffer[headerPos + i] = 0x00;
                    log?.Invoke($"[引擎] 已屏蔽 ModSecret::ServerSub 云端公告与强制自更新！");
                    patchedAny = true;
                }
                else
                {
                    byte[] patternServerSub212 = new byte[] {
                        0x7e, 0x45, 0x06, 0x00, 0x04, 0x72, 0x8f, 0xe8, 0x00, 0x70, 0x28, 0xc5, 0x00, 0x00, 0x0a
                    };
                    int idxServerSub212 = IndexOfSequence(buffer, offset, length, patternServerSub212);
                    if (idxServerSub212 != -1 && idxServerSub212 >= offset + 12)
                    {
                        int headerPos = idxServerSub212 - 12;
                        buffer[headerPos] = 0x06;
                        buffer[headerPos + 1] = 0x2A;
                        for (int i = 2; i < 12; i++) buffer[headerPos + i] = 0x00;
                        log?.Invoke($"[引擎] 已屏蔽 2.12.2 ModSecret::ServerSub 云端公告！");
                        patchedAny = true;
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"[引擎] 修补 ServerSub 提示: {ex.Message}");
            }

            try
            {
                byte[] patternShowLog = new byte[] {
                    0x03, 0x20, 0x9e, 0x01, 0x00, 0x00, 0x2f, 0x11, 0x09, 0x1b
                };
                int idxShowLog = IndexOfSequence(buffer, offset, length, patternShowLog);
                if (idxShowLog != -1)
                {
                    buffer[idxShowLog + 6] = 0x2B; // bge.s -> br.s
                    log?.Invoke($"[引擎] 已移除更新日志中的“暂不提供MC下载”提示！");
                    patchedAny = true;
                }
            }
            catch { }

            return patchedAny;
        }

        private static int IndexOfSequence(byte[] buffer, int offset, int length, byte[] pattern)
        {
            int end = offset + length - pattern.Length;
            byte p0 = pattern[0];
            for (int i = offset; i <= end; i++)
            {
                if (buffer[i] == p0)
                {
                    bool match = true;
                    for (int j = 1; j < pattern.Length; j++)
                    {
                        if (buffer[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }
                    if (match) return i;
                }
            }
            return -1;
        }
    }
}
