using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit;

namespace MBBSEmu.Tests.CPU
{
    public class FADD_Tests : CpuTestBase
    {

        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(0, 1, 1)]
        [InlineData(1, 1, 2)]
        [InlineData(1.5, 1.5, 3)]
        [InlineData(1, 1.5, 2.5)]
        [InlineData(0, 0.5, 0.5)]
        [InlineData(0.5, 0, 0.5)]
        [InlineData(float.MaxValue, 0, float.MaxValue)]
        [InlineData(float.MinValue, 0, float.MinValue)]
        [InlineData(float.MaxValue, float.MaxValue, float.PositiveInfinity)]
        public void FADD_Test(float ST0Value, float inputValue, float expectedValue)
        {
            Reset();

            //Set Values
            mbbsEmuCpuCore.FpuStack[0] = BitConverter.GetBytes(ST0Value);
            mbbsEmuCpuCore.Registers.DS = 2;
            CreateDataSegment(BitConverter.GetBytes(inputValue));

            //FADD 0x2:0x0
            CreateCodeSegment(new byte[] { 0xD8, 0x6, 0x0, 0x0 });

            //Process Instruction
            mbbsEmuCpuCore.Tick();

            Assert.Equal(expectedValue, BitConverter.ToSingle(mbbsEmuCpuCore.FpuStack[0]));
        }
    }
}
