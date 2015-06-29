using System;

namespace VirtualFileSystem.DeviceAdapter
{
    public abstract class AbstractDevice
    {
        public abstract UInt32 Size();

        public abstract T Read<T>(long position) where T : struct;

        public abstract T[] ReadArray<T>(long position, int count) where T : struct;

        public abstract void ReadArray<T>(long position, T[] array, int offset, int count) where T : struct;
        
        public abstract void Write<T>(long position, T structure) where T : struct;

        public abstract void WriteArray<T>(long position, T[] array, int offset, int count) where T : struct;
    }
}
