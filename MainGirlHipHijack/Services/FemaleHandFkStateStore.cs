using MainGirlHipHijack.Runtime;

namespace MainGirlHipHijack.Services
{
    internal sealed class FemaleHandFkStateStore
    {
        internal FemaleHandFkStateStore(int handCount, int layerCount)
        {
            Hands = new FemaleHandFkRuntimeState[handCount];
            for (int i = 0; i < handCount; i++)
                Hands[i] = new FemaleHandFkRuntimeState(layerCount);
        }

        internal FemaleHandFkRuntimeState[] Hands { get; }
    }
}
