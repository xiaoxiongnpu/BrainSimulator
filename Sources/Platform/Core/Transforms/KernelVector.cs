﻿

namespace GoodAI.Modules.Transforms
{
    //names here need to be exaclty same as the kernel names in Math\Vector, they are used as strings when searching for the kernels
    public enum KernelVector
    {
        ScalarAdd,
        ScalarAdd_Segmented,
        ScalarMult,
        ScalarMultThenAdd,
        ElementwiseAbs,
        ElementwiseThreshold,
        ElementwiseAdd,
        ElementwiseAdd_Bounded,
        ElementwiseAdd_BoundedWeighted,
        ElementwiseAdd_Weighted,
        ElementwiseAdd_WeightedOffsetted,
        ElementwiseDiv,
        ElementwiseMult,
        ElementwiseMult_Segmented,
        ElementwiseSub,
        CrossMult,
        OtherAverage
    }
}
