﻿using System;
using System.Collections.Generic;
using System.Text;
using Xunit;

namespace MBBSEmu.Tests.CPU
{
    public class Stack_Tests : CpuTestBase
    {
        [Fact]
        public void Push_Pop_Single_Value_Byte()
        {
            Reset();
            mbbsEmuMemoryCore.AddSegment(0); //Stack Segment
            mbbsEmuCpuCore.Push(byte.MaxValue);
            Assert.Equal(byte.MaxValue, mbbsEmuCpuCore.Pop());
        }

        [Fact]
        public void Push_Pop_Single_Value_Ushort()
        {
            Reset();
            mbbsEmuMemoryCore.AddSegment(0); //Stack Segment
            mbbsEmuCpuCore.Push(ushort.MaxValue);
            Assert.Equal(ushort.MaxValue, mbbsEmuCpuCore.Pop());
        }
    }
}
