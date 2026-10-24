/*
 * Bandizip 7.47 专业版解锁补丁
 * ============================================================
 * 目标  : Bandizip 7.47.0.1 (x64)，内部版本号 71912
 * 原理  : Bandizip 把「版本等级」按奇偶位打散后存在设置对象 +0x120：
 *           0x98   -> 100    标准版 STD
 *           0x3D4  -> 1000   专业版 PRO
 *           0x1B20 -> 10000  企业版 ENT
 *         全程序仅两处写入 0x98：
 *           0x1317F5  (ApplyEdition)        默认值写入
 *           0x15FF9C  (许可证校验 · 降级)    校验失败时降级
 *         把这两处立即数改成 0x3D4，即可常驻专业版。
 * 定位  : 使用「前一条指令 + 目标指令」组成的唯一上下文签名，
 *         不依赖硬编码偏移；命中 0 次或多次直接中止。
 * 构建  : build.bat  或  build.ps1  ->  dist\Bandizip747ProPatch.exe
 * 许可  : MIT
 * ============================================================
 */

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: System.Reflection.AssemblyTitle("Bandizip 7.47 专业版解锁补丁")]
[assembly: System.Reflection.AssemblyProduct("Bandizip747ProPatch")]
[assembly: System.Reflection.AssemblyDescription("Bandizip 7.47.0.1 x64 专业版解锁 / 还原工具")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: System.Reflection.AssemblyFileVersion("1.0.0.0")]

namespace BandizipProPatch
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string forced = null;
            bool auto = false;
            foreach (string a in args)
            {
                if (a.StartsWith("--exe=", StringComparison.OrdinalIgnoreCase))
                    forced = a.Substring(6).Trim('"');
                else if (a.Equals("--auto", StringComparison.OrdinalIgnoreCase))
                    auto = true;
            }
            Application.Run(new MainForm(forced, auto));
        }
    }

    internal static class PatchEngine
    {
        // 补丁点 1：ApplyEdition() 里 "非 PRO/ENT 一律按标准版" 的默认写入
        //   mov dword ptr [rsi+0x120], 0x98   (0x98 = 标准版 100)
        // 补丁点 2：许可证校验失败后的降级写入
        //   mov dword ptr [rax+0x120], 0x98
        // 两处都改成 0x3d4 (专业版 1000)。签名带上前一条指令做上下文，保证唯一。
        private static readonly string[] OldHex =
        {
            "488D88C0000000E85B77EEFFC7862001000098000000",
            "E89410FDFFC7802001000098000000"
        };

        private static readonly string[] NewHex =
        {
            "488D88C0000000E85B77EEFFC78620010000D4030000",
            "E89410FDFFC78020010000D4030000"
        };

        public const string BackupSuffix = ".bak";

        public static string[] OldPatterns { get { return OldHex; } }
        public static string[] NewPatterns { get { return NewHex; } }

        public static byte[] FromHex(string hex)
        {
            byte[] b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++)
                b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        public static int Count(byte[] data, byte[] pat)
        {
            int n = 0, pos = 0;
            while (true)
            {
                int i = IndexOf(data, pat, pos);
                if (i < 0) break;
                n++;
                pos = i + 1;
            }
            return n;
        }

        public static int IndexOf(byte[] data, byte[] pat, int start)
        {
            int limit = data.Length - pat.Length;
            for (int i = start; i <= limit; i++)
            {
                if (data[i] != pat[0]) continue;
                bool ok = true;
                for (int j = 1; j < pat.Length; j++)
                {
                    if (data[i + j] != pat[j]) { ok = false; break; }
                }
                if (ok) return i;
            }
            return -1;
        }

        public enum State { Missing, Original, Patched, Mixed }

        public static State Detect(string exePath)
        {
            if (!File.Exists(exePath)) return State.Missing;
            byte[] d = File.ReadAllBytes(exePath);
            int oldHits = 0, newHits = 0;
            for (int i = 0; i < OldHex.Length; i++)
            {
                oldHits += Count(d, FromHex(OldHex[i]));
                newHits += Count(d, FromHex(NewHex[i]));
            }
            if (oldHits == OldHex.Length && newHits == 0) return State.Original;
            if (newHits == NewHex.Length && oldHits == 0) return State.Patched;
            return State.Mixed;
        }

        public static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] h = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in h) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }

        public static void KillBandizip()
        {
            string[] names = { "Bandizip", "Arkview", "bz" };
            foreach (string n in names)
            {
                foreach (Process p in Process.GetProcessesByName(n))
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            System.Threading.Thread.Sleep(400);
        }

        public static bool Apply(string exePath, Action<string> log)
        {
            if (!File.Exists(exePath))
            {
                log("找不到文件：" + exePath);
                return false;
            }

            State st = Detect(exePath);
            if (st == State.Patched)
            {
                log("当前已经是专业版补丁状态，无需重复处理。");
                return true;
            }
            if (st == State.Missing)
            {
                log("文件不存在。");
                return false;
            }
            if (st == State.Mixed)
            {
                log("文件特征不匹配，可能已经被其它工具修改或版本不符，已中止以免损坏文件。");
                return false;
            }

            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(exePath);
            log("目标版本：" + vi.FileVersion + " / 产品版本 " + vi.ProductVersion);
            if (!vi.FileVersion.StartsWith("7.47", StringComparison.Ordinal))
            {
                log("提示：本补丁针对 Bandizip 7.47 制作，当前版本不是 7.47，可能匹配失败。");
            }

            log("正在关闭 Bandizip ...");
            KillBandizip();

            string bak = exePath + BackupSuffix;
            if (!File.Exists(bak))
            {
                log("备份原文件 -> " + Path.GetFileName(bak));
                File.Copy(exePath, bak, false);
            }
            else
            {
                log("已存在备份，保留不覆盖：" + Path.GetFileName(bak));
            }

            log("原文件 SHA256：" + Sha256(exePath));

            byte[] data = File.ReadAllBytes(exePath);
            bool changed = false;

            for (int i = 0; i < OldHex.Length; i++)
            {
                byte[] pat = FromHex(OldHex[i]);
                byte[] rep = FromHex(NewHex[i]);
                int hits = Count(data, pat);
                if (hits == 0)
                {
                    log("补丁点 " + (i + 1) + "：未找到特征，终止。");
                    return false;
                }
                if (hits > 1)
                {
                    log("补丁点 " + (i + 1) + "：特征重复 " + hits + " 次，终止以免误改。");
                    return false;
                }
                int at = IndexOf(data, pat, 0);
                Array.Copy(rep, 0, data, at, rep.Length);
                changed = true;
                log("补丁点 " + (i + 1) + "：文件偏移 0x" + at.ToString("X8") + " 写入完成");
            }

            if (!changed)
            {
                log("没有需要写入的内容。");
                return false;
            }

            FileAttributes attr = File.GetAttributes(exePath);
            if ((attr & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(exePath, attr & ~FileAttributes.ReadOnly);

            File.WriteAllBytes(exePath, data);
            System.Threading.Thread.Sleep(200);

            State after = Detect(exePath);
            log("校验结果：" + (after == State.Patched ? "成功，专业版补丁已生效" : "失败，特征与预期不符"));
            log("补丁后 SHA256：" + Sha256(exePath));
            return after == State.Patched;
        }

        public static bool Restore(string exePath, Action<string> log)
        {
            if (!File.Exists(exePath))
            {
                log("找不到文件：" + exePath);
                return false;
            }
            string bak = exePath + BackupSuffix;
            KillBandizip();

            if (File.Exists(bak))
            {
                log("从备份还原：" + Path.GetFileName(bak));
                File.Copy(bak, exePath, true);
                log("还原完成，SHA256：" + Sha256(exePath));
                return Detect(exePath) == State.Original;
            }

            log("未找到备份，尝试按特征反向还原 ...");
            byte[] data = File.ReadAllBytes(exePath);
            bool changed = false;
            for (int i = 0; i < NewHex.Length; i++)
            {
                byte[] pat = FromHex(NewHex[i]);
                byte[] rep = FromHex(OldHex[i]);
                int hits = Count(data, pat);
                if (hits != 1) continue;
                int at = IndexOf(data, pat, 0);
                Array.Copy(rep, 0, data, at, rep.Length);
                changed = true;
                log("反向写入：偏移 0x" + at.ToString("X8"));
            }
            if (!changed)
            {
                log("没有可还原的特征，文件可能已是原版。");
                return Detect(exePath) == State.Original;
            }
            File.WriteAllBytes(exePath, data);
            State st = Detect(exePath);
            log("还原结果：" + (st == State.Original ? "成功" : "未完全还原"));
            return st == State.Original;
        }
    }

    internal class MainForm : Form
    {
        private readonly TextBox txtPath = new TextBox();
        private readonly TextBox txtLog = new TextBox();
        private readonly Label lblState = new Label();
        private readonly Label lblVer = new Label();
        private readonly Button btnPatch = new Button();
        private readonly Button btnRestore = new Button();
        private readonly Button btnBrowse = new Button();
        private readonly Button btnOpen = new Button();
        private readonly Button btnExit = new Button();

        private string exePath;

        public MainForm(string forced, bool auto)
        {
            Text = "Bandizip 7.47 专业版解锁补丁 v1.0";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(720, 520);
            MinimumSize = new Size(720, 520);
            Font = new Font("Microsoft YaHei UI", 9f);
            BackColor = Color.FromArgb(24, 26, 32);
            ForeColor = Color.Gainsboro;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            Label title = new Label();
            title.Text = "Bandizip 7.47  专业版解锁补丁";
            title.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(93, 190, 255);
            title.SetBounds(20, 16, 500, 34);
            Controls.Add(title);

            Label sub = new Label();
            sub.Text = "签名匹配 · 自动备份 · 一键还原   (仅适用于 7.47.0.1 x64)";
            sub.ForeColor = Color.Gray;
            sub.SetBounds(22, 52, 600, 20);
            Controls.Add(sub);

            Label l1 = new Label();
            l1.Text = "程序路径";
            l1.SetBounds(20, 86, 70, 22);
            Controls.Add(l1);

            txtPath.SetBounds(94, 84, 500, 24);
            txtPath.ReadOnly = true;
            txtPath.BackColor = Color.FromArgb(38, 41, 48);
            txtPath.ForeColor = Color.Gainsboro;
            txtPath.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(txtPath);

            btnBrowse.Text = "浏览";
            btnBrowse.SetBounds(604, 83, 96, 26);
            btnBrowse.FlatStyle = FlatStyle.Flat;
            btnBrowse.FlatAppearance.BorderColor = Color.FromArgb(80, 86, 96);
            btnBrowse.BackColor = Color.FromArgb(44, 48, 56);
            btnBrowse.ForeColor = Color.Gainsboro;
            btnBrowse.Click += delegate { Browse(); };
            Controls.Add(btnBrowse);

            lblVer.SetBounds(94, 116, 600, 20);
            lblVer.ForeColor = Color.Gray;
            Controls.Add(lblVer);

            lblState.SetBounds(94, 140, 600, 24);
            lblState.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            Controls.Add(lblState);

            int y = 176;
            StyleButton(btnPatch, "一键解锁专业版", 20, y, 160, 34);
            btnPatch.BackColor = Color.FromArgb(38, 110, 180);
            btnPatch.ForeColor = Color.White;
            btnPatch.Click += delegate { DoPatch(); };
            Controls.Add(btnPatch);

            StyleButton(btnRestore, "还原原版", 190, y, 120, 34);
            btnRestore.Click += delegate { DoRestore(); };
            Controls.Add(btnRestore);

            StyleButton(btnOpen, "打开安装目录", 320, y, 130, 34);
            btnOpen.Click += delegate
            {
                if (exePath != null && File.Exists(exePath))
                    Process.Start("explorer.exe", "/select,\"" + exePath + "\"");
            };
            Controls.Add(btnOpen);

            StyleButton(btnExit, "退出", 460, y, 90, 34);
            btnExit.Click += delegate { Close(); };
            Controls.Add(btnExit);

            txtLog.SetBounds(20, 226, 680, 274);
            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.BackColor = Color.FromArgb(16, 18, 22);
            txtLog.ForeColor = Color.FromArgb(120, 220, 140);
            txtLog.Font = new Font("Consolas", 9f);
            txtLog.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(txtLog);

            exePath = string.IsNullOrEmpty(forced) ? FindBandizip() : forced;
            RefreshState();

            if (auto)
            {
                BeginInvoke((MethodInvoker)delegate { DoPatch(); });
            }
        }

        private static bool IsAdmin()
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static bool InProgramFiles(string path)
        {
            string full = Path.GetFullPath(path);
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(pf) && full.StartsWith(pf, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(pf86) && full.StartsWith(pf86, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private void Elevate(string path)
        {
            Log("该文件位于受保护目录，正在以管理员身份重新启动 ...");
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = Application.ExecutablePath;
                psi.Arguments = "--exe=\"" + path + "\" --auto";
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                Close();
            }
            catch
            {
                Log("提权被取消，无法继续。请右键本程序选择“以管理员身份运行”。");
                MessageBox.Show(this, "需要管理员权限。请右键本程序，选择“以管理员身份运行”。", "提示");
            }
        }

        private static void StyleButton(Button b, string text, int x, int y, int w, int h)
        {
            b.Text = text;
            b.SetBounds(x, y, w, h);
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 86, 96);
            b.BackColor = Color.FromArgb(44, 48, 56);
            b.ForeColor = Color.Gainsboro;
        }

        private static string FindBandizip()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Bandizip"))
                {
                    if (k != null)
                    {
                        string p = k.GetValue("ProgramFolder") as string;
                        if (!string.IsNullOrEmpty(p))
                        {
                            string exe = Path.Combine(p, "Bandizip.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Bandizip"))
                {
                    if (k != null)
                    {
                        string p = k.GetValue("ProgramFolder") as string;
                        if (!string.IsNullOrEmpty(p))
                        {
                            string exe = Path.Combine(p, "Bandizip.exe");
                            if (File.Exists(exe)) return exe;
                        }
                    }
                }
            }
            catch { }

            string self = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Bandizip.exe");
            if (File.Exists(self)) return self;

            string[] guess =
            {
                @"C:\Program Files\Bandizip\Bandizip.exe",
                @"C:\Program Files (x86)\Bandizip\Bandizip.exe"
            };
            foreach (string g in guess)
                if (File.Exists(g)) return g;

            return null;
        }

        private void Browse()
        {
            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Title = "选择 Bandizip.exe";
                dlg.Filter = "Bandizip.exe|Bandizip.exe|可执行文件 (*.exe)|*.exe";
                if (exePath != null && File.Exists(exePath))
                    dlg.InitialDirectory = Path.GetDirectoryName(exePath);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    exePath = dlg.FileName;
                    RefreshState();
                }
            }
        }

        private void Log(string s)
        {
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        private void RefreshState()
        {
            txtPath.Text = exePath ?? "(未找到)";
            if (exePath == null || !File.Exists(exePath))
            {
                lblVer.Text = "";
                lblState.Text = "状态：未找到 Bandizip.exe";
                lblState.ForeColor = Color.IndianRed;
                return;
            }
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(exePath);
            lblVer.Text = "文件版本：" + vi.FileVersion + "   产品版本：" + vi.ProductVersion;

            PatchEngine.State st = PatchEngine.Detect(exePath);
            if (st == PatchEngine.State.Original)
            {
                lblState.Text = "状态：原版（标准版 / 未解锁）";
                lblState.ForeColor = Color.FromArgb(255, 200, 90);
            }
            else if (st == PatchEngine.State.Patched)
            {
                lblState.Text = "状态：已解锁（专业版 Professional）";
                lblState.ForeColor = Color.FromArgb(110, 220, 130);
            }
            else
            {
                lblState.Text = "状态：未知（版本不同或已被其它程序修改）";
                lblState.ForeColor = Color.IndianRed;
            }
        }

        private bool CanWrite(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) { }
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return true; }   // 被 Bandizip 占用，Apply 会先结束进程
        }

        private bool EnsureReady(string path)
        {
            if (!IsAdmin() && InProgramFiles(path))
            {
                Elevate(path);
                return false;
            }
            if (!CanWrite(path))
            {
                if (!IsAdmin())
                {
                    Elevate(path);
                    return false;
                }
                Log("文件不可写（可能被占用或设置了只读），请关闭 Bandizip 后重试。");
                return false;
            }
            return true;
        }

        private void DoPatch()
        {
            if (exePath == null || !File.Exists(exePath))
            {
                MessageBox.Show(this, "没有找到 Bandizip.exe，请先点“浏览”选择。", "提示");
                return;
            }
            if (!EnsureReady(exePath)) { RefreshState(); return; }

            btnPatch.Enabled = false;
            try
            {
                Log("========== 开始解锁 ==========");
                bool ok = PatchEngine.Apply(exePath, Log);
                Log(ok ? "完成，重新打开 Bandizip 即可看到 (Professional)。" : "处理失败，详见日志。");
                MessageBox.Show(this, ok ? "解锁完成，重新启动 Bandizip 生效。" : "解锁失败，详见日志。", ok ? "成功" : "失败");
            }
            finally
            {
                btnPatch.Enabled = true;
                RefreshState();
            }
        }

        private void DoRestore()
        {
            if (exePath == null || !File.Exists(exePath))
            {
                MessageBox.Show(this, "没有找到 Bandizip.exe。", "提示");
                return;
            }
            if (!EnsureReady(exePath)) { RefreshState(); return; }

            btnRestore.Enabled = false;
            try
            {
                Log("========== 开始还原 ==========");
                bool ok = PatchEngine.Restore(exePath, Log);
                MessageBox.Show(this, ok ? "已还原为原版。" : "还原未完全成功，详见日志。", ok ? "完成" : "提示");
            }
            finally
            {
                btnRestore.Enabled = true;
                RefreshState();
            }
        }
    }
}

