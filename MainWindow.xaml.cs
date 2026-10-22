using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace PCLFixer
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log("====================================================");
            Log("🎉 欢迎使用 PCL2 原版MC下载限制一键修复工具！");
            Log("【功能】一键解除“暂时不再提供MC下载功能”限制，恢复原版下载与版本列表。");
            Log("【用法】工具会自动识别电脑中的 PCL2，直接点击【立即开始修复】即可。");
            Log("====================================================");
            Log("准备就绪，正在快速检索当前电脑中的 PCL2 启动器...");

            Task.Run(() =>
            {
                RunScan(false);
            });
        }

        private void BtnHelp_Click(object sender, RoutedEventArgs e)
        {
            string helpMsg = 
                "【PCL2 原版游戏下载一键修复工具 说明】\n\n" +
                "1. 适用场景：\n" +
                "   打开 PCL2 下载游戏时，弹出“为遵守相关法律法规，PCL暂时不再提供MC下载功能”提示并被阻止下载。\n\n" +
                "2. 修复原理：\n" +
                "   本工具通过动态特征码重构与核心运行库重写，恢复原版 Minecraft 官方版本列表与下载器绑定，并屏蔽云端停服公告下发与强制提示。\n\n" +
                "3. 常见问题：\n" +
                "   问：修复需要联网吗？\n" +
                "   答：不需要！工具内部已完整打包解禁运行库，纯离线秒级完成。\n\n" +
                "   问：会不会误删我的存档、模组或账号？\n" +
                "   答：绝对不会！工具只针对启动器核心执行修复，对原文件有自动备份 (.bak)，不改动任何游戏数据。\n\n" +
                "   问：修复后再次打开会不会又提示？\n" +
                "   答：工具已彻底切断下发该提示的云端接口并清空本地公告缓存，请放心使用！";

            MessageBox.Show(helpMsg, "使用说明与常见问题", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
                TxtLog.AppendText(line + Environment.NewLine);
                ScrollerLog.ScrollToEnd();
            });
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "选择 Plain Craft Launcher 2 启动器程序",
                Filter = "PCL2 启动器 (*Plain Craft Launcher*.exe; PCL.exe)|*Plain Craft Launcher*.exe;PCL.exe;._cache_*.exe|所有可执行文件 (*.exe)|*.exe"
            };

            if (ofd.ShowDialog() == true)
            {
                ComboPaths.Text = ofd.FileName;
                Log($"已手动选择目标: {ofd.FileName}");
            }
        }

        private void BtnScan_Click(object sender, RoutedEventArgs e)
        {
            BtnScan.IsEnabled = false;
            BtnStartFix.IsEnabled = false;
            ProgressBarStatus.IsIndeterminate = true;

            Task.Run(() =>
            {
                RunScan(true);
                Dispatcher.Invoke(() =>
                {
                    BtnScan.IsEnabled = true;
                    BtnStartFix.IsEnabled = true;
                    ProgressBarStatus.IsIndeterminate = false;
                });
            });
        }

        private void RunScan(bool verbose)
        {
            Log("开始扫描系统中的 PCL2 启动器文件...");
            var found = PclPatcherEngine.FindPclLaunchers(s => Log(s));
            string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";

            string currentDirExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Plain Craft Launcher 2.exe");
            if (File.Exists(currentDirExe) && !string.Equals(currentDirExe, currentExePath, StringComparison.OrdinalIgnoreCase) && !found.Contains(currentDirExe))
            {
                found.Insert(0, currentDirExe);
            }

            Dispatcher.Invoke(() =>
            {
                ComboPaths.Items.Clear();
                int preferredIndex = -1;
                for (int i = 0; i < found.Count; i++)
                {
                    string f = found[i];
                    ComboPaths.Items.Add(f);
                    string name = Path.GetFileName(f);
                    if (!name.StartsWith("._cache_") && preferredIndex == -1)
                    {
                        preferredIndex = i;
                    }
                }

                if (ComboPaths.Items.Count > 0)
                {
                    ComboPaths.SelectedIndex = preferredIndex >= 0 ? preferredIndex : 0;
                    Log($"扫描完成，找到 {found.Count} 处 PCL2 安装位置！已自动选中启动器主程序。");
                }
                else
                {
                    if (verbose)
                    {
                        Log("未在常用目录中发现 PCL2，请点击【浏览...】按钮手动定位 Plain Craft Launcher 2.exe 所在路径。");
                    }
                }
            });
        }

        private async void BtnStartFix_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = ComboPaths.Text.Trim();
            if (string.IsNullOrEmpty(targetPath))
            {
                MessageBox.Show("请先选择或浏览输入 PCL2 启动器的可执行文件路径！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(targetPath))
            {
                MessageBox.Show($"指定的目标文件不存在：\n{targetPath}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            BtnStartFix.IsEnabled = false;
            BtnBrowse.IsEnabled = false;
            BtnScan.IsEnabled = false;
            ProgressBarStatus.IsIndeterminate = true;

            Log("========== 开始一键修复 ==========");

            var result = await Task.Run(() =>
            {
                PclPatcherEngine.KillPclProcesses(s => Log(s));
                return PclPatcherEngine.FixPclInstallation(targetPath, s => Log(s));
            });

            ProgressBarStatus.IsIndeterminate = false;
            ProgressBarStatus.Value = result.Success ? 100 : 0;
            BtnStartFix.IsEnabled = true;
            BtnBrowse.IsEnabled = true;
            BtnScan.IsEnabled = true;

            if (result.Success)
            {
                MessageBox.Show("🎉 修复完成！\n\n限制提示已彻底移除，原版MC下载与版本列表已完美恢复。\n现在您可以正常打开启动器进入【下载】页面了！", 
                    "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"修复未完成：\n{result.Message}\n请查看详细日志排查原因。", 
                    "修复失败", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnOpenDir_Click(object sender, RoutedEventArgs e)
        {
            string targetPath = ComboPaths.Text.Trim();
            if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
            {
                string dir = Path.GetDirectoryName(targetPath);
                Process.Start("explorer.exe", $"/select,\"{targetPath}\"");
            }
            else
            {
                Process.Start("explorer.exe", AppDomain.CurrentDomain.BaseDirectory);
            }
        }
    }
}
