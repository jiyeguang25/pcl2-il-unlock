using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

// PCL2 官方版解锁补丁器（单文件版）
// 用法：把本 EXE 与官方 Plain Craft Launcher 2.exe（2.99.0.0 正式版）放同一目录，双击运行。
// 原理：内嵌一个由官方开源仓库同 commit 构建的"干净 donor"（仅 RestrictMcDownload=False，无任何凭据），
//       用 dnlib 把 donor 中 5 个被常量折叠掏空的方法体移植回官方 exe，其余字节与元数据保持官方原状。

namespace PCL2UnlockPatcher
{
    internal static class Program
    {
        // 要移植的方法（ownerType / methodName / 参数个数；-1 表示不限）
        private static readonly Tuple<string, string, int>[] Targets =
        {
            Tuple.Create("PCL.PageDownloadInstall", "LoaderInit", 0),
            Tuple.Create("PCL.ModModpack", "ModpackInstall", 0),
            Tuple.Create("PCL.ModModpack", "ModpackInstall", 3),
            Tuple.Create("PCL.PageDownloadModify", "LoadInstanceFinished", 1),
            Tuple.Create("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1),
        };

        private static int Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += ResolveEmbedded;   // 必须在任何 dnlib 类型 JIT 前挂上
            return RunCore(args);   // dnlib 类型引用全部隔离在本方法之外，确保 handler 先行
        }

        private static int RunCore(string[] args)
        {
            Console.Title = "PCL2 官方版解锁补丁器";
            Console.WriteLine("==============================================");
            Console.WriteLine("  PCL2 官方版解锁补丁器  v1.0");
            Console.WriteLine("  恢复 PCL 2.99.0.0 被移除的 MC 下载功能");
            Console.WriteLine("  （IL 方法体移植，保留官方凭据与全部设置）");
            Console.WriteLine("==============================================");
            Console.WriteLine();

            string here = AppDomain.CurrentDomain.BaseDirectory;
            string target = args.Length > 0 ? args[0] : Path.Combine(here, "Plain Craft Launcher 2.exe");

            if (!File.Exists(target))
            {
                Fail("未找到 Plain Craft Launcher 2.exe。\n请把本程序与 PCL2 官方版主程序放在同一目录后重新运行。\n（或把 exe 路径作为命令行参数传入）", 2);
                return 2;
            }

            // ---- 校验目标 ----
            string sha;
            using (var fs = File.OpenRead(target))
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
                sha = ToHex(sha256.ComputeHash(fs));

            string version;
            try { version = AssemblyName.GetAssemblyName(target).Version.ToString(4); }
            catch { Fail("目标文件不是有效的 .NET 程序集：" + target, 3); return 3; }

            Console.WriteLine("目标文件: " + target);
            Console.WriteLine("程序版本: " + version);
            Console.WriteLine("SHA256  : " + sha);
            Console.WriteLine();

            if (version != "2.99.0.0")
            {
                Fail("本补丁器仅支持 PCL2 2.99.0.0 正式版。\n当前版本为 " + version + "，新版本请到项目仓库获取更新的补丁器。", 4);
                return 4;
            }

            if (sha == "469D89BBBEF7589A60786E160836F327D353CBE18800F3748241DBD0D16766C2")
            {
                Console.WriteLine("[OK] 该文件已经是解锁版，无需重复补丁。");
                Console.WriteLine();
                WaitKey();
                return 0;
            }

            bool knownOfficial =
                sha == "6B6E1260CB5A1D6A303855F1C60CAB3D0BC1AEBBA7E40A964180188B271E7386" ||
                sha == "F56234F35753058C6B48B49258BEF13AC7CBBBC5D4A5A83C51AF6954B4B79A0A"; // 占位：其他官方分发渠道哈希可在发现后追加

            if (!knownOfficial)
            {
                Console.WriteLine("[提示] 该哈希不在已知的官方 2.99.0.0 清单中（可能是其他渠道的分发版）。");
                Console.WriteLine("       补丁按方法签名定位，理论上仍可工作；若补丁后异常请用备份还原。");
                Console.Write("       仍要继续吗？(Y/N) ");
                var k = Console.ReadKey();
                Console.WriteLine();
                if (k.Key != ConsoleKey.Y) { Console.WriteLine("已取消。"); WaitKey(); return 5; }
            }

            // ---- 解出内嵌 donor ----
            Console.WriteLine();
            Console.WriteLine("[1/4] 释放内嵌的参考构建（donor）…");
            byte[] donorBytes;
            string donorPath = Path.Combine(Path.GetTempPath(), "PCL2Unlock_donor_" + Guid.NewGuid().ToString("N") + ".exe");
            try
            {
                donorBytes = LoadEmbeddedDonor();
                using (var input = new MemoryStream(donorBytes))
                using (var ds = new DeflateStream(input, CompressionMode.Decompress))
                using (var output = File.Create(donorPath))
                    ds.CopyTo(output);
                Console.WriteLine("      donor: " + ((long)donorBytes.Length).ToString() + " bytes（压缩）→ " + new FileInfo(donorPath).Length + " bytes");
            }
            catch (Exception ex)
            {
                Fail("释放 donor 失败：" + ex.Message, 6);
                return 6;
            }

            try
            {
                // ---- 打补丁 ----
                Console.WriteLine("[2/4] 加载官方 exe 与 donor 的元数据…");
                var official = ModuleDefMD.Load(target);
                var donor = ModuleDefMD.Load(donorPath);
                Console.WriteLine("      官方: " + official.Types.Count() + " 类型, donor: " + donor.Types.Count() + " 类型");

                Console.WriteLine("[3/4] 移植 5 个方法体…");
                int ported = 0;
                foreach (var t in Targets)
                {
                    var oType = official.Types.FirstOrDefault(x => x.FullName == t.Item1);
                    var dType = donor.Types.FirstOrDefault(x => x.FullName == t.Item1);
                    if (oType == null || dType == null)
                    {
                        Console.WriteLine("      [跳过] 类型未找到: " + t.Item1);
                        continue;
                    }
                    IEnumerable<MethodDef> oMs = oType.Methods.Where(m => m.Name == t.Item2);
                    IEnumerable<MethodDef> dMs = dType.Methods.Where(m => m.Name == t.Item2);
                    if (t.Item3 >= 0)
                    {
                        oMs = oMs.Where(m => m.Parameters.Count == (m.IsStatic ? t.Item3 : t.Item3 + 1));
                        dMs = dMs.Where(m => m.Parameters.Count == (m.IsStatic ? t.Item3 : t.Item3 + 1));
                    }
                    var oList = oMs.ToList();
                    var dList = dMs.ToList();
                    if (oList.Count != 1 || dList.Count != 1)
                    {
                        Console.WriteLine("      [跳过] " + t.Item1 + "." + t.Item2 + " 匹配数不唯一 (官方 " + oList.Count + ", donor " + dList.Count + ")");
                        continue;
                    }
                    var om = oList[0];
                    var dm = dList[0];
                    int before = om.Body != null ? om.Body.Instructions.Count : 0;
                    Port(official, donor, om, dm);
                    ported++;
                    Console.WriteLine("      [移植] " + t.Item2 + "  IL " + before + " → " + om.Body.Instructions.Count);
                }
                if (ported == 0) { Fail("没有任何方法被移植——官方 exe 结构可能与预期不符，已中止。", 7); return 7; }

                Console.WriteLine("[4/4] 写出补丁版 exe…");
                string outPath = target + ".unlocked";
                var opts = new ModuleWriterOptions(official) { Logger = DummyLogger.NoThrowInstance };
                official.Write(outPath, opts);
                Console.WriteLine("      输出: " + outPath);

                // ---- 备份并替换 ----
                string backup = target + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Copy(target, backup, true);
                File.Delete(target);
                File.Move(outPath, target);
                Console.WriteLine();
                Console.WriteLine("==============================================");
                Console.WriteLine("  补丁完成！共移植 " + ported + " 个方法体。");
                Console.WriteLine("  原版备份: " + Path.GetFileName(backup));
                Console.WriteLine("  直接启动 PCL2 即可使用下载功能。");
                Console.WriteLine("  如需还原：把 .bak 文件改名回原文件名即可。");
                Console.WriteLine("==============================================");
                WaitKey();
                return 0;
            }
            catch (Exception ex)
            {
                Fail("补丁失败：" + ex.Message + "\n" + ex.StackTrace, 8);
                return 8;
            }
            finally
            {
                try { if (File.Exists(donorPath)) File.Delete(donorPath); } catch { }
            }
        }

        // 从内嵌资源加载 dnlib（实现真正的单文件）
        private static Assembly ResolveEmbedded(object sender, ResolveEventArgs e)
        {
            if (!e.Name.StartsWith("dnlib", StringComparison.OrdinalIgnoreCase)) return null;
            var asm = Assembly.GetExecutingAssembly();
            using (var s = asm.GetManifestResourceStream("PCL2Unlock.dnlib.dll"))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return Assembly.Load(ms.ToArray());
            }
        }

        // —— donor 方法体 → 官方 exe（与已验证的 MethodPorter 逻辑一致）——
        private static void Port(ModuleDef target, ModuleDef donorMod, MethodDef om, MethodDef dm)
        {
            var importer = new Importer(target);
            var newBody = new CilBody { InitLocals = dm.Body.InitLocals, MaxStack = dm.Body.MaxStack };
            var instrMap = new Dictionary<Instruction, Instruction>();
            var newInstrs = new List<Instruction>();
            foreach (var i in dm.Body.Instructions)
            {
                var ni = new Instruction(i.OpCode, null);
                instrMap[i] = ni;
                newInstrs.Add(ni);
            }
            var varMap = new Dictionary<Local, Local>();
            foreach (var lv in dm.Body.Variables)
            {
                var nl = new Local(importer.Import(lv.Type));
                newBody.Variables.Add(nl);
                varMap[lv] = nl;
            }
            foreach (var i in dm.Body.Instructions)
            {
                var ni = instrMap[i];
                var op = i.Operand;
                if (op == null) ni.Operand = null;
                else if (op is Instruction t1) ni.Operand = instrMap[t1];
                else if (op is Instruction[] t2) ni.Operand = t2.Select(x => instrMap[x]).ToArray();
                else if (op is Local loc) ni.Operand = varMap[loc];
                else if (op is ITypeDefOrRef tdr) ni.Operand = importer.Import(tdr);
                else if (op is IMethod im) ni.Operand = importer.Import(im);
                else if (op is IField fd) ni.Operand = importer.Import(fd);
                else if (op is string s) ni.Operand = s;
                else if (op is int n32) ni.Operand = n32;
                else if (op is long n64) ni.Operand = n64;
                else if (op is float f32) ni.Operand = f32;
                else if (op is double f64) ni.Operand = f64;
                else if (op is sbyte i8) ni.Operand = i8;
                else if (op is byte u8) ni.Operand = u8;
                else throw new Exception("不支持的操作数类型: " + op.GetType().FullName);
            }
            foreach (var ni in newInstrs) newBody.Instructions.Add(ni);
            foreach (var eh in dm.Body.ExceptionHandlers)
            {
                var neh = new ExceptionHandler(eh.HandlerType)
                {
                    TryStart = instrMap[eh.TryStart],
                    TryEnd = eh.TryEnd == null ? null : instrMap[eh.TryEnd],
                    HandlerStart = instrMap[eh.HandlerStart],
                    HandlerEnd = eh.HandlerEnd == null ? null : instrMap[eh.HandlerEnd],
                    CatchType = eh.CatchType == null ? null : importer.Import(eh.CatchType)
                };
                newBody.ExceptionHandlers.Add(neh);
            }
            om.Body = newBody;
        }

        private static byte[] LoadEmbeddedDonor()
        {
            var asm = Assembly.GetExecutingAssembly();
            var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("donor.deflate", StringComparison.OrdinalIgnoreCase));
            if (resName == null) throw new Exception("找不到内嵌 donor 资源");
            using (var s = asm.GetManifestResourceStream(resName))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static string ToHex(byte[] data)
        {
            var sb = new StringBuilder(data.Length * 2);
            foreach (var b in data) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }

        private static void Fail(string msg, int code)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("[错误] " + msg);
            Console.ResetColor();
            WaitKey();
        }

        private static void WaitKey()
        {
            Console.WriteLine();
            Console.Write("按任意键退出…");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
