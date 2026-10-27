using System;
using MainGameVoiceFaceEventBridge.Game;

namespace MainGameVoiceFaceEventBridge.RegressionHarness
{
    internal static partial class Program
    {
        private static void CheckGameVoiceLoadAdmission()
        {
            var ledger = new GameVoiceLoadLedger<object>();
            Action<bool, string> check = (ok, message) => { _assertions++; if (!ok) throw new Exception(message); };
            object first = new object(), second = new object();
            var a = ledger.Begin(first);
            check(ledger.HasPending, "native load blocks cue before any AudioSource exists");
            var b = ledger.Begin(second);
            ledger.Finish(a);
            check(ledger.HasPending, "finishing one native load preserves the other's reservation");
            ledger.CancelWhere(owner => ReferenceEquals(owner, second));
            check(!ledger.HasPending && ledger.IsCancelled(second), "stop releases reservation and rejects late native completion");
            ledger.Finish(b);
            check(!ledger.HasPending, "failed or cancelled completion cannot leave a permanent block");

            a = ledger.Begin(first);
            ledger.Reset();
            check(!ledger.HasPending && ledger.IsCancelled(first), "scene exit releases load but remembers its cancellation");
            b = ledger.Begin(second);
            ledger.Finish(a);
            check(ledger.HasPending && !ledger.IsCancelled(second), "old scene completion cannot release a new scene's load");
            ledger.Finish(b);
            check(!ledger.HasPending, "all completed loads release admission");

            a = ledger.Begin(first);
            ledger.CancelWhere(owner => ReferenceEquals(owner, second));
            check(ledger.HasPending && !ledger.IsCancelled(first), "stopping another character leaves this voice protected");
            ledger.Finish(a);
            ledger.Finish(a);
            check(!ledger.HasPending, "duplicate cleanup is harmless");
        }
    }
}
