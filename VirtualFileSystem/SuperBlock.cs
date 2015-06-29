using System;
using System.Runtime.InteropServices;

namespace VirtualFileSystem
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct _SuperBlock
    {
        /// <summary>
        /// 可以容纳多少个 inode
        /// </summary>
        public UInt32 inodeCapacity;
        
        /// <summary>
        /// 已经分配了多少个 inode
        /// </summary>
        public UInt32 inodeAllocated;

        /// <summary>
        /// 每个数据块大小
        /// </summary>
        public UInt16 blockSize;

        /// <summary>
        /// 可以容纳多少个数据块
        /// </summary>
        public UInt32 blockCapacity;

        /// <summary>
        /// 已经预留了多少个数据块
        /// </summary>
        public UInt32 blockPreserved;

        /// <summary>
        /// 已经实际分配了多少个数据块
        /// </summary>
        public UInt32 blockAllocated;

        /// <summary>
        /// 有效标识符，必须为 0x1234
        /// 标识符放在最后，这样 SuperBlock 之前字段数量变更后就会要求重新格式化，避免潜在数据问题
        /// </summary>
        public UInt16 magicValue;

        /// <summary>
        /// 是否是一个有效的结构
        /// </summary>
        /// <returns></returns>
        public Boolean IsValid()
        {
            return magicValue == 0x1234;
        }

        /// <summary>
        /// 初始化一个全新的 SuperBlock
        /// </summary>
        /// <param name="inodeCapacity"></param>
        /// <param name="blockSize"></param>
        /// <param name="blockCapacity"></param>
        public _SuperBlock(UInt32 inodeCapacity,UInt16 blockSize, UInt32 blockCapacity)
        {
            this.inodeCapacity = inodeCapacity;
            this.inodeAllocated = 0;
            this.blockSize = blockSize;
            this.blockCapacity = blockCapacity;
            this.blockPreserved = 0;
            this.blockAllocated = 0;
            this.magicValue = 0x1234;
        }
    }

    public class SuperBlock
    {
        /// <summary>
        /// 可持久化数据
        /// </summary>
        public _SuperBlock data;
        
        /// <summary>
        /// inode 位图起始地址
        /// </summary>
        public UInt32 pInodeBitVectors;

        /// <summary>
        /// inode 区起始地址
        /// </summary>
        public UInt32 pInodeData;

        /// <summary>
        /// 数据块位图起始地址
        /// </summary>
        public UInt32 pBlockBitVectors;

        /// <summary>
        /// 数据块区起始地址
        /// </summary>
        public UInt32 pBlockData;

        /// <summary>
        /// 存储介质
        /// </summary>
        private VFSCore vfs;

        public SuperBlock(VFSCore vfs, _SuperBlock data)
        {
            this.vfs = vfs;
            this.data = data;

            if (data.IsValid())
            {
                init();
            }
        }

        /// <summary>
        /// 根据有效的持久化数据初始化整个结构
        /// </summary>
        private void init()
        {
            uint offset = 0;
            offset += (uint)Utils.GetStructSize<_SuperBlock>();

            pInodeBitVectors = offset;
            offset += ((data.inodeCapacity / 32) + 1) * 4;

            pInodeData = offset;
            offset += data.inodeCapacity * (uint)Utils.GetStructSize<_INode>();

            pBlockBitVectors = offset;
            offset += ((data.blockCapacity / 32) + 1) * 4;

            pBlockData = offset;
        }
        
        /// <summary>
        /// 将 superblock 写入存储介质
        /// </summary>
        /// <param name="vfs"></param>
        public void Save()
        {
            vfs.GetDevice().Write(0, data);
        }
        
        /// <summary>
        /// 转换为字符串
        /// </summary>
        /// <returns></returns>
        public override String ToString()
        {
            return String.Format("sizeof(_superBlock) = {8}, sizeof(_inode) = {9}, " +
                "inode 数 = {0}, 数据块大小 = {1} byte, 数据块数 = {2} (可容纳 {7} MB 数据), " + 
                "p_inodeBitmap = {3}, p_inodeData = {4}, p_blockBitmap = {5}, p_blockData = {6}",
                data.inodeCapacity, data.blockSize, data.blockCapacity,
                pInodeBitVectors, pInodeData, pBlockBitVectors, pBlockData,
                data.blockSize * data.blockCapacity >> 20,
                Utils.GetStructSize<_SuperBlock>(), Utils.GetStructSize<_INode>());
        }

        /// <summary>
        /// 从存储介质上还原 SuperBlock
        /// </summary>
        /// <param name="vfs"></param>
        /// <returns></returns>
        public static SuperBlock Load(VFSCore vfs)
        {
            var _superBlock = vfs.GetDevice().Read<_SuperBlock>(0);
            return new SuperBlock(vfs, _superBlock);
        }

        /// <summary>
        /// 创建一个全新的 SuperBlock
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="inodeCapacity"></param>
        /// <param name="blockSize"></param>
        /// <param name="blockCapacity"></param>
        /// <returns></returns>
        public static SuperBlock Create(VFSCore vfs, UInt32 inodeCapacity, UInt16 blockSize, UInt32 blockCapacity)
        {
            var _superBlock = new _SuperBlock(inodeCapacity, blockSize, blockCapacity);
            var superBlock = new SuperBlock(vfs, _superBlock);
            superBlock.Save();
            return superBlock;
        }
    }
}
