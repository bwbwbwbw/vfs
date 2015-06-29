using System;
using System.IO;
using System.IO.MemoryMappedFiles;

namespace VirtualFileSystem.DeviceAdapter
{
    // Use physics file as the underlayer storage
    public class FileAdapter : AbstractDevice
    {
        private UInt32 sizeByte;
        private MemoryMappedFile mmf = null;

        private MemoryMappedViewAccessor accessor = null;

        /// <summary>
        /// 物理文件适配器，若不存在文件，则会创建一个新文件并打开，若已存在，则会打开文件。
        /// 使用 Memory Mapping 技术提高随机访问性能。
        /// </summary>
        /// <param name="path">存储文件名</param>
        /// <param name="sizeByte">预分配文件大小</param>
        public FileAdapter(String path, UInt32 sizeByte)
        {
            this.sizeByte = sizeByte;

            if (!File.Exists(path))
            {
                var file = File.Create(path);
                file.SetLength(sizeByte);
                file.Close();
            }

            mmf = MemoryMappedFile.CreateFromFile(path, FileMode.Open);
            accessor = mmf.CreateViewAccessor();
        }
        
        public override UInt32 Size()
        {
            return sizeByte;
        }

        public override T Read<T>(long position)
        {
            int size = Utils.GetStructSize<T>();
            byte[] data = new byte[size];
            accessor.ReadArray(position, data, 0, size);

            return Utils.ByteToStruct<T>(data);
        }

        public override T[] ReadArray<T>(long position, int count)
        {
            var tt = new T[count];
            accessor.ReadArray(position, tt, 0, count);
            return tt;
        }

        public override void ReadArray<T>(long position, T[] array, int offset, int count)
        {
            accessor.ReadArray(position, array, offset, count);
        }

        public override void Write<T>(long position, T structure)
        {
            byte[] arr = Utils.StructToByte(structure);
            accessor.WriteArray(position, arr, 0, arr.Length);
        }

        public override void WriteArray<T>(long position, T[] array, int offset, int count)
        {
            accessor.WriteArray(position, array, offset, count);
        }
        
        ~FileAdapter()
        {
        }
    }
}
