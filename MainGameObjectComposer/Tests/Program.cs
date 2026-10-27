using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MainGameObjectComposer;
using Mono.Cecil;

internal static class Program
{
    private sealed class Target { internal float Position; }
    private sealed class Dan
    {
        internal int Calls;
        internal float LastPosition;
        internal bool LastTwoDans;
        internal bool Throw;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void SetDanTarget(Target target, bool twoDans)
        {
            Calls++;
            LastPosition = target.Position;
            LastTwoDans = twoDans;
            if (Throw) throw new InvalidOperationException("original failed");
        }
    }

    private static DeferredTargetQueue<PenetrationFollowContext> _queue;
    private static int _frame;
    private static bool _follow;
    private static bool _valid;
    private static PenetrationFollowContext _context;
    private static readonly MethodInfo SetTarget = typeof(Dan).GetMethod("SetDanTarget", BindingFlags.Instance | BindingFlags.NonPublic);
    private static int _passed;

    // Same signature and reflection re-entry path as the real BP hook.
    private static bool Prefix(object __instance, object __0, bool __1)
    {
        return _queue.ShouldRunOriginal(_frame, __instance, __0, __1, _context, _follow);
    }

    private static void Reset()
    {
        _queue = new DeferredTargetQueue<PenetrationFollowContext>();
        _frame = 1;
        _follow = true;
        _valid = true;
        _context = new PenetrationFollowContext
        {
            Scene = new object(), Female = new object(), Male = new object(),
            FemaleBody = new object(), MaleBody = new object(), ReferenceTarget = new object(),
            ComposerTarget = new object(), Animation = new object(), Preset = new object()
        };
    }

    private static void Flush()
    {
        _queue.Drain(_frame, call => _valid && _follow && call.Context.Matches(_context),
            call => SetTarget.Invoke(call.Agent, new[] { call.Target, (object)call.TwoDans }));
    }

    private static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception("FAIL: " + name);
        _passed++;
        Console.WriteLine("PASS: " + name);
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--movement")
        {
            try { MovementTests.Run(Check); Console.WriteLine("Movement checks passed=" + _passed); return 0; }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        var harmony = new Harmony("composer.betterpenetration.tests");
        try
        {
            harmony.Patch(SetTarget, prefix: new HarmonyMethod(typeof(Program), nameof(Prefix)));
            RunSchedulerTests();
            if (args.Length == 2) CheckInstalledContract(args[0], args[1]);
            else throw new Exception("Pass installed BP DLL and built Composer DLL paths.");
            Console.WriteLine("Passed " + _passed + " checks (headless; no in-game visual test).");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { harmony.UnpatchSelf(); }
    }

    private static void RunSchedulerTests()
    {
        Reset();
        var dan = new Dan();
        var target = new Target { Position = 1 };
        dan.SetDanTarget(target, false);
        dan.SetDanTarget(target, true);
        Check(dan.Calls == 0, "follow ON defers all early calls");
        target.Position = 9; // Simulate IK moving the live target after the early call.
        Flush();
        Check(dan.Calls == 1 && dan.LastPosition == 9 && dan.LastTwoDans,
            "one late call reads changed position and preserves latest arguments");
        Flush();
        dan.SetDanTarget(target, false);
        Check(dan.Calls == 1, "repeat drain / patched re-entry / same-frame extra call cannot double-apply");
        _frame++;
        dan.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 2, "next frame runs once again");

        Reset();
        dan = new Dan();
        _follow = false;
        dan.SetDanTarget(target, false);
        Check(dan.Calls == 1, "follow OFF immediately runs the original");
        Flush();
        Check(dan.Calls == 1, "follow OFF has no late replay");
        _follow = true;
        dan.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 1, "enabling after ordinary calculation cannot apply twice");

        Reset();
        dan = new Dan();
        dan.SetDanTarget(target, false);
        _follow = false;
        dan.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 1, "disabling with a fresh BP call cancels pending and runs once normally");
        _follow = true;
        dan.SetDanTarget(target, false);
        Check(dan.Calls == 1, "ON/OFF/ON in one frame cannot double-apply");

        Reset();
        dan = new Dan();
        dan.SetDanTarget(target, false);
        _follow = false;
        Flush();
        Check(dan.Calls == 0, "disabling before flush drops the queued call");
        _frame++;
        dan.SetDanTarget(target, false);
        Check(dan.Calls == 1, "next frame after disabling uses normal timing");

        Reset();
        dan = new Dan();
        dan.SetDanTarget(target, false);
        _frame++;
        Flush();
        Check(dan.Calls == 0, "missed dispatcher never replays previous-frame work");
        dan.SetDanTarget(target, false);
        Check(dan.Calls == 1, "first call after late phase passes through in that frame");
        Flush();
        Check(dan.Calls == 1, "late arrival is never queued into the next frame");

        foreach (FieldInfo field in typeof(PenetrationFollowContext).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            Reset();
            dan = new Dan();
            dan.SetDanTarget(target, false);
            object replaced = _context;
            field.SetValue(replaced, new object());
            _context = (PenetrationFollowContext)replaced;
            Flush();
            Check(dan.Calls == 0, field.Name + ": changed identity drops stale call");
            _frame++;
            dan.SetDanTarget(target, false);
            Flush();
            Check(dan.Calls == 1, field.Name + ": new context works next frame");
        }

        Reset();
        dan = new Dan();
        dan.SetDanTarget(target, false);
        _valid = false;
        Flush();
        Check(dan.Calls == 0, "removed BP agents / different woman are rejected at flush");

        Reset();
        dan = new Dan();
        dan.SetDanTarget(target, false);
        _queue.Clear();
        Flush();
        Check(dan.Calls == 0, "runtime cleanup discards pending references");

        Reset();
        dan = new Dan();
        var other = new Dan();
        var otherTarget = new Target { Position = 42 };
        dan.SetDanTarget(target, false);
        dan.SetDanTarget(otherTarget, true);
        other.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 1 && dan.LastPosition == 42 && other.Calls == 1,
            "latest target wins; independent male agents each run once");

        Reset();
        dan = new Dan { Throw = true };
        dan.SetDanTarget(target, false);
        try { Flush(); } catch (TargetInvocationException) { }
        dan.Throw = false;
        dan.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 1, "failed original is not retried or left in replay mode");
        _frame++;
        dan.SetDanTarget(target, false);
        Flush();
        Check(dan.Calls == 2, "replay guard recovers after original throws");
    }

    private static void CheckInstalledContract(string bpPath, string composerPath)
    {
        using (var bp = AssemblyDefinition.ReadAssembly(bpPath))
        {
            var types = bp.MainModule.Types;
            var dan = types.Single(t => t.FullName == "Core_BetterPenetration.DanAgent");
            var collision = types.Single(t => t.FullName == "Core_BetterPenetration.CollisionAgent");
            var core = types.Single(t => t.FullName == "Core_BetterPenetration.CoreGame");
            var plugin = types.Single(t => t.FullName == "KKS_BetterPenetration.KKS_BetterPenetration");
            Check(dan.Methods.Any(m => m.Name == "SetDanTarget" && !m.IsStatic
                && m.ReturnType.FullName == "System.Void" && m.Parameters.Count == 2
                && m.Parameters[0].ParameterType.FullName == collision.FullName
                && m.Parameters[1].ParameterType.FullName == "System.Boolean"), "installed BP SetDanTarget signature");
            Check(dan.Fields.Any(f => f.Name == "m_danCharacter" && f.FieldType.FullName == "ChaControl" && !f.IsStatic)
                && dan.Fields.Any(f => f.Name == "m_referenceTarget" && f.FieldType.FullName == "UnityEngine.Transform" && !f.IsStatic)
                && collision.Fields.Any(f => f.Name == "m_collisionCharacter" && f.FieldType.FullName == "ChaControl" && !f.IsStatic),
                "installed BP character and target fields");
            Check(new[] { "danAgents", "collisionAgents", "danHasNewTarget" }.All(name => core.Fields.Any(f =>
                f.Name == name && f.IsStatic && f.FieldType.FullName.StartsWith("System.Collections.Generic.List`1<"))),
                "installed BP live agent lists");
            Check(plugin.Fields.Any(f => f.Name == "hSceneProc" && f.IsStatic && f.FieldType.FullName == "UnityEngine.MonoBehaviour")
                && new[] { "inHScene", "loadingCharacter" }.All(name => plugin.Fields.Any(f =>
                    f.Name == name && f.IsStatic && f.FieldType.FullName == "System.Boolean")), "installed BP session guards");
        }
        using (var composer = AssemblyDefinition.ReadAssembly(composerPath))
        {
            var dispatcher = composer.MainModule.Types.Single(t => t.Name == "BetterPenetrationLateFollow");
            var order = dispatcher.CustomAttributes.Single(a => a.AttributeType.FullName == "UnityEngine.DefaultExecutionOrder");
            Check((int)order.ConstructorArguments[0].Value == 12150, "built dispatcher execution order is 12150");
            Check(!composer.MainModule.AssemblyReferences.Any(a => a.Name == "KKS_BetterPenetration"),
                "built Composer has no hard BP assembly dependency");
            Check(!composer.MainModule.Types.Any(t => t.Name == "Program"), "test code excluded from deployed plugin");
        }
    }
}
