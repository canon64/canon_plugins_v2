using MainGirlHipHijack.Runtime;

namespace MainGirlHipHijack.Services
{
    internal sealed class BodyIkStateStore
    {
        internal BodyIkStateStore(int operationPointCount)
        {
            OperationPoints = new BodyIkOperationPointState[operationPointCount];
            Wanted = new bool[operationPointCount];
            Weights = new float[operationPointCount];
            for (int i = 0; i < operationPointCount; i++)
                OperationPoints[i] = new BodyIkOperationPointState();
        }

        internal BodyIkOperationPointState[] OperationPoints { get; }
        internal bool[] Wanted { get; }
        internal float[] Weights { get; }
    }
}
