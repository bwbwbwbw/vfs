using System;

namespace VirtualFileSystem
{
    public class Block
    {
        /// <summary>
        /// 该数据块编号
        /// </summary>
        public UInt32 index;

        /// <summary>
        /// 存储介质
        /// </summary>
        private VFSCore vfs;
        
        public Block(VFSCore vfs, UInt32 index = UInt32.MaxValue)
        {
            this.vfs = vfs;
            this.index = index;
        }
        
        public UInt32 GetPosition(UInt32 offset)
        {
            return vfs.GetSuperBlock().pBlockData + index * vfs.GetSuperBlock().data.blockSize + offset;
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockId"></waram>
        /// <param name="offset"></param>
        /// <returns></returns>
        public T Read<T>(UInt32 offset) where T : struct
        {
            UInt32 position = GetPosition(offset);
            return vfs.GetDevice().Read<T>(position);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockId"></param>
        /// <param name="offset"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        public T[] ReadArray<T>(UInt32 offset, int count) where T : struct
        {
            UInt32 position = GetPosition(offset);
            return vfs.GetDevice().ReadArray<T>(position, count);
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockId"></param>
        /// <param name="offset"></param>
        /// <param name="array"></param>
        /// <param name="arrOffset"></param>
        /// <param name="count"></param>
        public void ReadArray<T>(UInt32 offset, T[] array, int arrOffset, int count) where T : struct
        {
            UInt32 position = GetPosition(offset);
            vfs.GetDevice().ReadArray(position, array, arrOffset, count);
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockId"></param>
        /// <param name="offset"></param>
        /// <param name="structure"></param>
        public void Write<T>(UInt32 offset, T structure) where T : struct
        {
            UInt32 position = GetPosition(offset);
            vfs.GetDevice().Write<T>(position, structure);
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="blockId"></param>
        /// <param name="offset"></param>
        /// <param name="array"></param>
        /// <param name="arrOffset"></param>
        /// <param name="count"></param>
        public void WriteArray<T>(UInt32 offset, T[] array, int arrOffset, int count) where T : struct
        {
            UInt32 position = GetPosition(offset);
            vfs.GetDevice().WriteArray(position, array, arrOffset, count);
        }

        /// <summary>
        /// 创建一个新的数据块
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static Block Create(VFSCore vfs, UInt32 index, Byte fill = 0)
        {
            if (index >= vfs.GetSuperBlock().data.blockCapacity)
            {
                throw new Exception("无效 block 编号");
            }
            Block block = new Block(vfs, index);

            Byte[] data = new Byte[vfs.GetSuperBlock().data.blockSize];
            for (var i = 0; i < data.Length; ++i)
            {
                data[i] = fill;
            }
            block.WriteArray(0, data, 0, data.Length);

            return block;
        }
    }
}
