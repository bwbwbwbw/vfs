using System;
using System.Linq;
using VirtualFileSystem.DeviceAdapter;

namespace VirtualFileSystem
{
    public class VFSCore
    {
        /// <summary>
        /// 存储介质大小（字节）
        /// </summary>
        private UInt32 deviceSize;

        /// <summary>
        /// 存储介质对象
        /// </summary>
        private AbstractDevice device;

        /// <summary>
        /// 超级块对象
        /// </summary>
        private SuperBlock superBlock;

        /// <summary>
        /// inode 位图
        /// </summary>
        private UInt32[] inodeBitmaps;
        
        /// <summary>
        /// 数据块位图
        /// </summary>
        private UInt32[] blockBitmaps;

        public VFSCore(AbstractDevice device)
        {
            this.device = device;
            this.deviceSize = device.Size();

            // 初始化超级块
            this.superBlock = SuperBlock.Load(this);
            
            if (IsFormated())
            {
                loadBitmaps();
                Console.WriteLine("载入镜像成功。");
                Console.WriteLine("SuperBlock: {0}", superBlock.ToString());
            }
        }

        /// <summary>
        /// 获取此文件系统的存储介质操作对象
        /// </summary>
        /// <returns></returns>
        public AbstractDevice GetDevice()
        {
            return device;
        }

        /// <summary>
        /// 获取此文件系统的超级块操作对象
        /// </summary>
        /// <returns></returns>
        public SuperBlock GetSuperBlock()
        {
            return superBlock;
        }

        /// <summary>
        /// 返回存储介质是否已格式化
        /// </summary>
        /// <returns></returns>
        public Boolean IsFormated()
        {
            return superBlock.data.IsValid();
        }

        /// <summary>
        /// 从已经格式化的存储介质中载入位图到内存
        /// </summary>
        private void loadBitmaps()
        {
            int inodeVectorCount = (int)superBlock.data.inodeCapacity / 32 + 1;
            inodeBitmaps = device.ReadArray<UInt32>(superBlock.pInodeBitVectors, inodeVectorCount);

            int blockVectorCount = (int)superBlock.data.blockCapacity / 32 + 1;
            blockBitmaps = device.ReadArray<UInt32>(superBlock.pBlockBitVectors, blockVectorCount);
        }

        /// <summary>
        /// 格式化存储介质
        /// </summary>
        /// <param name="inodeCapacity">inode 数量</param>
        /// <param name="blockSizeKByte">block 大小（KB），必须为 1，2，4，8 中的一个</param>
        public void Format(UInt32 inodeCapacity, UInt16 blockSizeKByte = 4)
        {
            if (inodeCapacity < 32)
            {
                throw new Exception("inode 至少为 32 个");
            }

            if (!new int[] { 1, 2, 4, 8 }.Contains(blockSizeKByte))
            {
                throw new Exception("block 大小只能为 1KB, 2KB, 4KB 或 8KB");
            }

            uint offset = 0;
            offset += (uint)Utils.GetStructSize<_SuperBlock>();
            offset += (inodeCapacity / 32 + 1) * 4;
            offset += (uint)Utils.GetStructSize<_INode>() * inodeCapacity;
            
            if (offset > deviceSize)
            {
                throw new Exception("inode 数量过大，结构超出存储介质最大空间");
            }

            // 可留给数据块位图和数据块区的大小
            uint sizeRemain = deviceSize - offset;

            // 全部留给数据块，可以有多少个数据块
            uint blockCapacity = sizeRemain / blockSizeKByte >> 10;

            if (blockCapacity < 128)
            {
                throw new Exception("磁盘空间太小，至少要可以容纳 128 个块");
            }

            // 删除 (blockCapacity / 32 + 1) * 4 大小的数据块，留作数据块位图使用
            blockCapacity -= ((blockCapacity / 32 + 1) * 4 / blockSizeKByte) + 1;

            // 初始化 superBlock
            superBlock = SuperBlock.Create(this, inodeCapacity, (UInt16)(blockSizeKByte << 10), blockCapacity);

            // 单个 inode BitVector 可容纳 32 位
            inodeBitmaps = new UInt32[inodeCapacity / 32 + 1];
            device.WriteArray(superBlock.pInodeBitVectors, inodeBitmaps, 0, inodeBitmaps.Length);

            // 单个 block BitVector 可容纳 32 位
            blockBitmaps = new UInt32[blockCapacity / 32 + 1];
            device.WriteArray(superBlock.pBlockBitVectors, blockBitmaps, 0, blockBitmaps.Length);

            // 写入根目录
            INodeDirectory.Create(this);

            Console.WriteLine("格式化成功。");
            Console.WriteLine("SuperBlock: {0}", superBlock.ToString());
        }

        /// <summary>
        /// 预留指定数量的数据块
        /// </summary>
        /// <param name="blocksToPreserve"></param>
        public void PreserveBlock(UInt32 blocksToPreserve)
        {
            if (blocksToPreserve == 0)
            {
                return;
            }
            if (superBlock.data.blockPreserved + blocksToPreserve > superBlock.data.blockCapacity)
            {
                throw new Exception("block 数量已满");
            }
            superBlock.data.blockPreserved += blocksToPreserve;
            superBlock.Save();
        }

        /// <summary>
        /// 减少预留指定数量的数据块
        /// </summary>
        /// <param name="blocksToDePreserve"></param>
        public void DePreserveBlock(UInt32 blocksToDePreserve)
        {
            if (blocksToDePreserve == 0)
            {
                return;
            }
            if (superBlock.data.blockPreserved < blocksToDePreserve)
            {
                throw new Exception("block 不足");
            }
            superBlock.data.blockPreserved -= blocksToDePreserve;
            superBlock.Save();
        }
        
        /// <summary>
        /// 位图指定位置是否已被分配
        /// </summary>
        /// <param name="bitmap"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        private Boolean IsBitmapAllocated(UInt32[] bitmap, UInt32 index)
        {
            if ((bitmap[index / 32] & (uint)1 << (int)(index % 32)) == 0)
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        /// <summary>
        /// 获取一个位图可用的位置，若无法找到则返回 Int32.MaxValue
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        private UInt32 GitBitmapAvailableIndex(UInt32[] bitmap)
        {
            for (int i = 0; i < bitmap.Length; ++i)
            {
                if (bitmap[i] != uint.MaxValue)
                {
                    for (int j = 0; j < 32; ++j)
                    {
                        if ((bitmap[i] & (1 << j)) == 0)
                        {
                            return (UInt32)(i * 32 + j);
                        }
                    }
                }
            }

            return UInt32.MaxValue;
        }

        /// <summary>
        /// 持久化一个位图向量
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="bitmap"></param>
        /// <param name="index"></param>
        private void UpdateBitmapVectorAtIndex(UInt32 offset, UInt32[] bitmap, UInt32 index)
        {
            device.Write(offset + index / 32 * 4, bitmap[index / 32]);
        }

        /// <summary>
        /// 位图指定位置标记为已占用
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="bitmap"></param>
        /// <param name="index"></param>
        private void SetBitmapAllocated(UInt32 offset, UInt32[] bitmap, UInt32 index)
        {
            bitmap[index / 32] |= (uint)1 << (int)(index % 32);
            UpdateBitmapVectorAtIndex(offset, bitmap, index);
        }

        /// <summary>
        /// 位图指定位置标记为未被占用
        /// </summary>
        /// <param name="offset"></param>
        /// <param name="bitmap"></param>
        /// <param name="index"></param>
        private void SetBitmapNotAllocated(UInt32 offset, UInt32[] bitmap, UInt32 index)
        {
            bitmap[index / 32] &= ~((uint)1 << (int)(index % 32));
            UpdateBitmapVectorAtIndex(offset, bitmap, index);
        }

        /// <summary>
        /// 判断 inode index 是否已被占用
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Boolean IsINodeAllocated(UInt32 index)
        {
            if (index == UInt32.MaxValue)
            {
                return false;
            }
            return IsBitmapAllocated(inodeBitmaps, index);
        }

        /// <summary>
        /// 判断 block inde
        /// </summary>
        /// <param name="index"></param>
        /// <returns></returns>
        public Boolean IsBlockAllocated(UInt32 index)
        {
            if (index == UInt32.MaxValue)
            {
                return false;
            }
            return IsBitmapAllocated(blockBitmaps, index);
        }

        /// <summary>
        /// 查找一个空闲 inode
        /// </summary>
        /// <returns></returns>
        private UInt32 GetFreeINodeIndex()
        {
            return GitBitmapAvailableIndex(inodeBitmaps);
        }

        /// <summary>
        /// 查找一个空闲块
        /// </summary>
        /// <returns></returns>
        private UInt32 GetFreeBlockIndex()
        {
            return GitBitmapAvailableIndex(blockBitmaps);
        }

        /// <summary>
        /// 分配、初始化一个新的 inode
        /// </summary>
        /// <returns></returns>
        public INode AllocateINode(UInt32 flags, UInt32 owner)
        {
            // 查找位图，寻找一个可用的 index
            UInt32 freeIndex = GetFreeINodeIndex();
            if (freeIndex == UInt32.MaxValue)
            {
                throw new Exception("inode 数量已满");
            }

            // 创建
            INode inode = INode.Create(this, freeIndex, flags, owner);

            // 置位
            SetBitmapAllocated(superBlock.pInodeBitVectors, inodeBitmaps, freeIndex);

            // 更新计数器
            superBlock.data.inodeAllocated++;
            superBlock.Save();

            return inode;
        }
        
        /// <summary>
        /// 分配一个新数据块并清空内容
        /// </summary>
        /// <returns></returns>
        public Block AllocateBlock(Byte fill = 0)
        {
            // 查找位图，寻找一个可用的 index
            UInt32 freeIndex = GetFreeBlockIndex();
            if (freeIndex == UInt32.MaxValue)
            {
                throw new Exception("block 数量已满");
            }

            // 创建
            Block block = Block.Create(this, freeIndex, fill);

            // 置位
            SetBitmapAllocated(superBlock.pBlockBitVectors, blockBitmaps, freeIndex);

            // 更新计数器
            superBlock.data.blockAllocated++;
            superBlock.Save();

            return block;
        }

        /// <summary>
        /// 收回一个 inode
        /// </summary>
        /// <param name="inodeIndex"></param>
        public void DeAllocateINode(UInt32 inodeIndex)
        {
            if (inodeIndex >= superBlock.data.inodeCapacity)
            {
                return;
            }
            if (!IsINodeAllocated(inodeIndex))
            {
                return;
            }

            // 位清零
            SetBitmapNotAllocated(superBlock.pInodeBitVectors, inodeBitmaps, inodeIndex);

            // 更新计数器
            superBlock.data.inodeAllocated--;
            superBlock.Save();
        }

        /// <summary>
        /// 收回一个数据块
        /// </summary>
        /// <param name="blockIndex"></param>
        public void DeAllocateBlock(UInt32 blockIndex)
        {
            if (blockIndex == UInt32.MaxValue)
            {
                Console.WriteLine("Warn: Deallocating NULL block");
                return;
            }
            if (blockIndex >= superBlock.data.blockCapacity)
            {
                Console.WriteLine("Warn: Deallocating block out of bound");
                return;
            }
            if (!IsBlockAllocated(blockIndex))
            {
                Console.WriteLine("Warn: Deallocating block {0} which is not allocated", blockIndex);
                return;
            }

            // 位清零
            SetBitmapNotAllocated(superBlock.pBlockBitVectors, blockBitmaps, blockIndex);

            // 更新计数器
            superBlock.data.blockAllocated--;
            superBlock.data.blockPreserved--;
            superBlock.Save();
        }
    }
}
