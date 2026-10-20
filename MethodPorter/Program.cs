using System;
using System.Collections.Generic;
using System.Linq;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

// MethodPorter: 把 donor exe 中指定方法的方法体移植进官方 exe（同源程序集，元数据可对齐）。
// 用法: MethodPorter <officialExe> <donorExe> <outputExe> [list|port]

class MethodPorter
{
    static readonly (string type, string method, int paramCount)[] Targets =
    {
        ("PCL.PageDownloadInstall", "LoaderInit", 0),          // 假加载器 → 真加载器
        ("PCL.ModModpack", "ModpackInstall", 0),                // Hint拦截 → SelectFile+RunInThread
        ("PCL.ModModpack", "ModpackInstall", 3),                // throw → 完整 switch 分发
        ("PCL.PageDownloadModify", "LoadInstanceFinished", 1),  // CardMinecraft.Collapsed → Visible
        ("PCL.PageDownloadResourceDetail", "InitializeStackResourceVersions", 1), // 版本列表无安装按钮 → 含 Install_Click
    };

    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("usage: MethodPorter <official> <donor> <output> [list|port]");
            return 1;
        }
        string mode = args.Length > 3 ? args[3] : "list";

        var official = ModuleDefMD.Load(args[0]);
        var donor = ModuleDefMD.Load(args[1]);

        Console.WriteLine($"official: {official.Types.Count()} types");
        Console.WriteLine($"donor:    {donor.Types.Count()} types");

        foreach (var (typeName, methodName, pcount) in Targets)
        {
            var oType = official.Types.FirstOrDefault(t => t.FullName == typeName);
            var dType = donor.Types.FirstOrDefault(t => t.FullName == typeName);
            if (oType == null || dType == null)
            {
                Console.WriteLine($"[MISS] type {typeName} not found (o:{oType != null} d:{dType != null})");
                continue;
            }
            IEnumerable<MethodDef> oMs = oType.Methods.Where(m => m.Name == methodName);
            IEnumerable<MethodDef> dMs = dType.Methods.Where(m => m.Name == methodName);
            if (pcount >= 0)
            {
                // 静态方法无 this 参数，实例方法多一个 this
                oMs = oMs.Where(m => m.Parameters.Count == (m.IsStatic ? pcount : pcount + 1));
                dMs = dMs.Where(m => m.Parameters.Count == (m.IsStatic ? pcount : pcount + 1));
            }
            var oList = oMs.ToList(); var dList = dMs.ToList();
            Console.WriteLine($"\n== {typeName}.{methodName}: official {oList.Count}, donor {dList.Count}");
            foreach (var om in oList)
                Console.WriteLine($"  [O] {(om.IsStatic ? "static " : "")}{om.FullName}  IL:{om.Body?.Instructions.Count ?? 0}");
            foreach (var dm in dList)
                Console.WriteLine($"  [D] {(dm.IsStatic ? "static " : "")}{dm.FullName}  IL:{dm.Body?.Instructions.Count ?? 0}");
            if (mode == "port" && oList.Count == 1 && dList.Count == 1)
            {
                Port(official, donor, oList[0], dList[0]);
            }
        }

        if (mode == "port")
        {
            var opts = new ModuleWriterOptions(official);
            opts.Logger = DummyLogger.NoThrowInstance;
            official.Write(args[2], opts);
            Console.WriteLine($"\nwritten: {args[2]}");
        }
        return 0;
    }

    static void Port(ModuleDef target, ModuleDef donorMod, MethodDef om, MethodDef dm)
    {
        var importer = new Importer(target);
        var newBody = new CilBody();
        newBody.InitLocals = dm.Body.InitLocals;
        newBody.MaxStack = dm.Body.MaxStack;
        var instrMap = new Dictionary<Instruction, Instruction>();
        var newInstrs = new List<Instruction>();
        foreach (var i in dm.Body.Instructions)
        {
            var ni = new Instruction(i.OpCode, null);
            instrMap[i] = ni;
            newInstrs.Add(ni);
        }
        // 先建局部变量表，再解析指令（指令可能引用局部变量槽位）
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
            ni.Operand = i.Operand switch
            {
                null => null,
                Instruction t1 => instrMap[t1],
                Instruction[] t2 => t2.Select(t => instrMap[t]).ToArray(),
                Local loc => varMap[loc], // ldloc/ldloca/stloc 引用局部变量
                ITypeDefOrRef tdr => importer.Import(tdr),
                IMethod im => importer.Import(im),
                IField fd => importer.Import(fd),
                string s => s,
                int n => n,
                long l => l,
                float f => f,
                double d => d,
                sbyte sb => sb,
                byte b => b,
                _ => throw new Exception("unsupported operand: " + i.Operand.GetType().FullName)
            };
        }
        foreach (var ni in newInstrs) newBody.Instructions.Add(ni);
        foreach (var eh in dm.Body.ExceptionHandlers)
        {
            var neh = new ExceptionHandler(eh.HandlerType);
            neh.TryStart = instrMap[eh.TryStart];
            neh.TryEnd = eh.TryEnd == null ? null : instrMap[eh.TryEnd];
            neh.HandlerStart = instrMap[eh.HandlerStart];
            neh.HandlerEnd = eh.HandlerEnd == null ? null : instrMap[eh.HandlerEnd];
            neh.CatchType = eh.CatchType == null ? null : importer.Import(eh.CatchType);
            newBody.ExceptionHandlers.Add(neh);
        }
        om.Body = newBody;
        Console.WriteLine($"  PORTED {om.FullName}  IL:{newBody.Instructions.Count}");
    }
}
