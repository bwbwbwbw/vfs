using System;
using System.Runtime.InteropServices;

namespace VirtualFileSystem
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    public struct _INode
    {
        /// <summary>
        /// 文件大小，字节为单位
        /// </summary>
        public UInt32 sizeByte;

        /// <summary>
        /// 最后访问时间戳
        /// </summary>
        public UInt64 accessTime;

        /// <summary>
        /// 最后修改时间戳
        /// </summary>
        public UInt64 modifyTime;

        /// <summary>
        /// 创建时间戳
        /// </summary>
        public UInt64 creationTime;

        /// <summary>
        /// 链接数
        /// </summary>
        public UInt32 linkCount;

        /// <summary>
        /// 属性，最低位0代表是文件，最低位1代表是目录
        /// </summary>
        public UInt32 flags;

        /// <summary>
        /// 所有者 ID
        /// </summary>
        public UInt32 owner;

        /// <summary>
        /// 数据块索引，前 12 块为直接索引，第 13 块为一次间接索引，第 14 块为二次间接索引
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 14)]
        public UInt32[] dataBlockId;

        /// <summary>
        /// 当前 inode 已预留多少个数据块
        /// </summary>
        public UInt32 blockPreserved;
        
        /// <summary>
        /// 初始化一个全新的 inode
        /// </summary>
        /// <param name="flags"></param>
        /// <param name="owner"></param>
        public _INode(UInt32 flags, UInt32 owner)
        {
            this.sizeByte = 0;
            this.accessTime = 0;
            this.modifyTime = 0;
            this.creationTime = (UInt64)DateTime.Now.Ticks;
            this.linkCount = 0;
            this.flags = flags;
            this.owner = owner;
            this.blockPreserved = 0;
            this.dataBlockId = new UInt32[14];

            // 0xFFFFFFFF 代表未分配
            for (int i = 0; i < 14; ++i)
            {
                dataBlockId[i] = UInt32.MaxValue;
            }
        }
    }

    public class INode
    {
        /// <summary>
        /// 可持久化数据
        /// </summary>
        public _INode data;

        /// <summary>
        /// 该 inode 编号
        /// </summary>
        public UInt32 index;

        /// <summary>
        /// 存储介质
        /// </summary>
        private VFSCore vfs;

        /// <summary>
        /// 使用直接索引的最大字节数
        /// </summary>
        private UInt32 BoundLv0;

        /// <summary>
        /// 使用一级间接索引的最大字节数
        /// </summary>
        private UInt32 BoundLv1;

        public INode(VFSCore vfs, _INode data, UInt32 index = UInt32.MaxValue)
        {
            this.vfs = vfs;
            this.data = data;
            this.index = index;

            UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
            UInt32 IndexPerBlock = blockSize / sizeof(UInt32);
            BoundLv0 = 12 * blockSize;
            BoundLv1 = BoundLv0 + IndexPerBlock * blockSize;
        }

        public UInt32 GetPosition()
        {
            return GetPosition(vfs, index);
        }

        public static UInt32 GetPosition(VFSCore vfs, UInt32 index)
        {
            return vfs.GetSuperBlock().pInodeData + index * (uint)Utils.GetStructSize<_INode>();
        }

        /// <summary>
        /// 该 inode 是否是一个目录
        /// </summary>
        /// <returns></returns>
        public Boolean IsDirectory()
        {
            return (data.flags & 1) == 1;
        }

        /// <summary>
        /// 返回当该 inode 总计要预留若干数据块时，还需要申请预留多少数据块
        /// </summary>
        /// <param name="nBlock"></param>
        /// <returns></returns>
        public UInt32 GetBlocksToPreserve(UInt32 nBlock)
        {
            if (nBlock < data.blockPreserved)
            {
                return 0;
            }
            else
            {
                return nBlock - data.blockPreserved;
            }
        }

        /// <summary>
        /// 查找指定偏移所在的块，若块不存在则返回无效块
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public Block GetBlockAtPosition(UInt32 position, Boolean allocate = false)
        {
            UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
            UInt32 IndexPerBlock = blockSize / sizeof(UInt32);
            
            if (position < BoundLv0)
            {
                // 直接索引
                UInt32 lv0_index = position / blockSize;
                return new Block(vfs, data.dataBlockId[lv0_index]);
            }
            else if (position < BoundLv1)
            {
                // 一级间接索引
                position -= BoundLv0;

                UInt32 lv0_index = 12;
                UInt32 lv1_index = position / blockSize;

                Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                if (lv0_block.index == UInt32.MaxValue)
                {
                    return lv0_block;
                }

                return new Block(vfs, lv0_block.Read<UInt32>(lv1_index * sizeof(UInt32)));
            }
            else
            {
                // 二级间接索引
                position -= BoundLv1;

                UInt32 lv0_index = 13;
                UInt32 lv1_index = (position / blockSize) / IndexPerBlock;
                UInt32 lv2_index = (position / blockSize) % IndexPerBlock;

                Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                if (lv0_block.index == UInt32.MaxValue)
                {
                    return lv0_block;
                }

                Block lv1_block = new Block(vfs, lv0_block.Read<UInt32>(lv1_index * sizeof(UInt32)));
                if (lv1_block.index == UInt32.MaxValue)
                {
                    return lv1_block;
                }

                return new Block(vfs, lv1_block.Read<UInt32>(lv2_index * sizeof(UInt32)));
            }
        }

        /// <summary>
        /// 修改并释放数据占用空间，删除第 newSize 字节开始直到原文件大小的数据块
        /// </summary>
        /// <param name="newSize"></param>
        public void Resize(UInt32 newSize)
        {
            try
            {
                // 写入时动态扩充，因此向更大大小扩展不做处理
                if (newSize >= data.sizeByte)
                {
                    return;
                }

                Console.WriteLine("Resizing inode {0} size from {1} to {2}...", index, data.sizeByte, newSize);

                UInt32 position = newSize;

                // 缩小占用空间
                UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
                UInt32 IndexPerBlock = blockSize / sizeof(UInt32);

                // 检查是否需要整理二级间接索引
                Int32 lv0_index = 13;
                Int32 lv1_index = -1;
                Int32 lv2_index = -1;
                if (position > BoundLv1)
                {
                    lv1_index = (int)(((position - BoundLv1) / blockSize) / IndexPerBlock);
                    lv2_index = (int)(((position - BoundLv1) / blockSize) % IndexPerBlock);
                }
                if (data.dataBlockId[lv0_index] != UInt32.MaxValue)
                {
                    //Console.WriteLine("Level 2 block recycling...");

                    // 读取其中所有项
                    Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                    UInt32[] lv1_block_id = lv0_block.ReadArray<UInt32>(0, (int)IndexPerBlock);

                    // 依次释放 lv1_index 之后所有的项
                    for (var i = lv1_index + 1; i < IndexPerBlock; ++i)
                    {
                        if (lv1_block_id[i] != UInt32.MaxValue)
                        {
                            Block lv1_block = new Block(vfs, lv1_block_id[i]);
                            UInt32[] lv2_block_id = lv1_block.ReadArray<UInt32>(0, (int)IndexPerBlock);

                            for (var j = 0; j < IndexPerBlock; ++j)
                            {
                                //Console.WriteLine(j);
                                //Console.WriteLine("{0}, {1}", j, lv2_block_id[j]);
                                if (lv2_block_id[j] != UInt32.MaxValue)
                                {
                                    //Console.WriteLine("Dealloc lv2 block: inode->{0}->{1}->{2}->{3}", lv0_index, lv0_block.index, lv1_block.index, lv2_block_id[j]);
                                    vfs.DeAllocateBlock(lv2_block_id[j]);
                                    data.blockPreserved--;
                                }
                            }

                            //Console.WriteLine("Dealloc lv1 block: inode->{0}->{1}->{2}", lv0_index, lv0_block.index, lv1_block.index);
                            vfs.DeAllocateBlock(lv1_block.index);
                            data.blockPreserved--;
                            lv0_block.Write((uint)(i * sizeof(UInt32)), UInt32.MaxValue);
                        }
                    }

                    // 释放 lv1_index 中 lv2_index 之后的所有项
                    if (lv1_index != -1 && lv1_block_id[lv1_index] != UInt32.MaxValue)
                    {
                        Block lv1_block = new Block(vfs, lv1_block_id[lv1_index]);
                        UInt32[] lv2_block_id = lv1_block.ReadArray<UInt32>(0, (int)IndexPerBlock);

                        for (var j = lv2_index; j < IndexPerBlock; ++j)
                        {
                            if (lv2_block_id[j] != UInt32.MaxValue)
                            {
                                //Console.WriteLine("Dealloc lv2 block: inode->{0}->{1}->{2}->{3}", lv0_index, lv0_block.index, lv1_block.index, lv2_block_id[j]);
                                vfs.DeAllocateBlock(lv2_block_id[j]);
                                data.blockPreserved--;
                                lv1_block.Write((uint)(j * sizeof(UInt32)), UInt32.MaxValue);
                            }
                        }
                    }

                    // 回收 inode.dataBlockId[13]
                    if (lv1_index == -1)
                    {
                        //Console.WriteLine("Dealloc lv0 block: inode->{0}->{1}", lv0_index, data.dataBlockId[lv0_index]);
                        vfs.DeAllocateBlock(data.dataBlockId[lv0_index]);
                        data.blockPreserved--;
                        data.dataBlockId[lv0_index] = UInt32.MaxValue;
                    }
                }

                // 是否需要整理一级索引
                lv0_index = 12;
                lv1_index = -1;
                if (position < BoundLv1)
                {
                    if (position > BoundLv0)
                    {
                        lv1_index = (int)((position - BoundLv0) / blockSize);
                    }
                    if (data.dataBlockId[lv0_index] != UInt32.MaxValue)
                    {
                        //Console.WriteLine("Level 1 block recycling...");

                        // 读取其中所有项
                        Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                        UInt32[] lv1_block_id = lv0_block.ReadArray<UInt32>(0, (int)IndexPerBlock);

                        // 依次释放 lv1_index 之后所有的项
                        for (var i = lv1_index + 1; i < IndexPerBlock; ++i)
                        {
                            if (lv1_block_id[i] != UInt32.MaxValue)
                            {
                                //Console.WriteLine("Dealloc lv1 block: inode->{0}->{1}->{2}", lv0_index, lv0_block.index, lv1_block_id[i]);
                                vfs.DeAllocateBlock(lv1_block_id[i]);
                                data.blockPreserved--;
                                lv0_block.Write((uint)(i * sizeof(UInt32)), UInt32.MaxValue);
                            }
                        }

                        // 回收 inode.dataBlockId[12]
                        if (lv1_index == -1)
                        {
                            //Console.WriteLine("Dealloc lv0 block: inode->{0}->{1}", lv0_index, data.dataBlockId[lv0_index]);
                            vfs.DeAllocateBlock(data.dataBlockId[lv0_index]);
                            data.blockPreserved--;
                            data.dataBlockId[lv0_index] = UInt32.MaxValue;
                        }
                    }
                }

                // 是否需要整理直接索引
                lv0_index = -1;
                if (position < BoundLv0)
                {
                    if (position > 0)
                    {
                        lv0_index = (int)(position / blockSize);
                    }
                    for (var i = lv0_index + 1; i < 12; ++i)
                    {
                        //Console.WriteLine("Level 0 block recycling...");

                        if (data.dataBlockId[i] != UInt32.MaxValue)
                        {
                            //Console.WriteLine("Dealloc lv0 block: inode->{0}->{1}", i, data.dataBlockId[i]);
                            vfs.DeAllocateBlock(data.dataBlockId[i]);
                            data.blockPreserved--;
                            data.dataBlockId[i] = UInt32.MaxValue;
                        }
                    }
                }

                // 计算新的 Preserved 值
                UInt32 totalBlocks = 0;

                if (position != 0)
                {
                    position--;

                    if (position < BoundLv0)
                    {
                        // 直接索引
                        lv0_index = (int)(position / blockSize);
                        totalBlocks += (uint)lv0_index + 1;
                    }
                    else if (position < BoundLv1)
                    {
                        // 一级间接索引
                        lv0_index = 12;
                        lv1_index = (int)((position - BoundLv0) / blockSize);

                        totalBlocks += (uint)lv0_index + 1;
                        totalBlocks += (uint)lv1_index + 1;
                    }
                    else
                    {
                        // 二级间接索引
                        lv0_index = 13;
                        lv1_index = (int)(((position - BoundLv1) / blockSize) / IndexPerBlock);
                        lv2_index = (int)(((position - BoundLv1) / blockSize) % IndexPerBlock);

                        totalBlocks += (uint)(lv0_index + 1);
                        totalBlocks += (uint)(IndexPerBlock + 1);
                        totalBlocks += (uint)((IndexPerBlock + 1) * lv1_index);
                        totalBlocks += (uint)(lv2_index + 1);
                    }
                }

                Console.WriteLine("Block to preserve old = {0}, new = {1}", data.blockPreserved, totalBlocks);

                // 更新预留块
                UInt32 deltaBlock = data.blockPreserved - totalBlocks;
                data.blockPreserved = totalBlocks;
                vfs.DePreserveBlock(deltaBlock);

                Console.WriteLine("Decreased preserved block {0}.", deltaBlock);

                data.sizeByte = newSize;
                data.modifyTime = (UInt64)DateTime.Now.Ticks;

                Save();

            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 查找指定偏移所在的块，若块不存在则创建
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        public Block PrepareBlockAtPosition(UInt32 position)
        {
            UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
            UInt32 IndexPerBlock = blockSize / sizeof(UInt32);

            // 直接索引
            if (position < BoundLv0)
            {
                UInt32 totalBlocks = 0;
                UInt32 lv0_index = position / blockSize;

                totalBlocks += lv0_index + 1;
                UInt32 preserve = GetBlocksToPreserve(totalBlocks);
                //Console.WriteLine("preserve block: {0} (+{1})", totalBlocks, preserve);

                if (preserve > 0)
                {
                    vfs.PreserveBlock(preserve);
                    data.blockPreserved += preserve;
                    //Console.WriteLine("data.blockPreserved = {0}", data.blockPreserved);
                    Save();
                }

                Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                if (lv0_block.index == UInt32.MaxValue)
                {
                    lv0_block = vfs.AllocateBlock();
                    data.dataBlockId[lv0_index] = lv0_block.index;
                    Save();
                }

                return lv0_block;
            }
            else if (position < BoundLv1)
            {
                // 一级间接索引
                position -= BoundLv0;

                UInt32 totalBlocks = 0;
                UInt32 lv0_index = 12;
                UInt32 lv1_index = position / blockSize;

                totalBlocks += lv0_index + 1;
                totalBlocks += lv1_index + 1;
                UInt32 preserve = GetBlocksToPreserve(totalBlocks);
                //Console.WriteLine("preserve block: {0} (+{1})", totalBlocks, preserve);

                if (preserve > 0)
                {
                    vfs.PreserveBlock(preserve);
                    data.blockPreserved += preserve;
                    //Console.WriteLine("data.blockPreserved = {0}", data.blockPreserved);
                    Save();
                }

                Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                if (lv0_block.index == UInt32.MaxValue)
                {
                    lv0_block = vfs.AllocateBlock(0xFF);
                    data.dataBlockId[lv0_index] = lv0_block.index;
                    Save();
                }

                Block lv1_block = new Block(vfs, lv0_block.Read<UInt32>(lv1_index * sizeof(UInt32)));
                if (lv1_block.index == UInt32.MaxValue)
                {
                    lv1_block = vfs.AllocateBlock();
                    lv0_block.Write(lv1_index * sizeof(UInt32), lv1_block.index);
                }

                return lv1_block;
            }
            else
            {
                // 二级间接索引
                position -= BoundLv1;

                UInt32 totalBlocks = 0;
                UInt32 lv0_index = 13;
                UInt32 lv1_index = (position / blockSize) / IndexPerBlock;
                UInt32 lv2_index = (position / blockSize) % IndexPerBlock;

                //Console.WriteLine("position = {0}, lv0_index = {1}, lv1_index = {2}, lv2_index = {3}", position, lv0_index, lv1_index, lv2_index);

                totalBlocks += lv0_index + 1;
                totalBlocks += IndexPerBlock + 1;
                totalBlocks += (IndexPerBlock + 1) * lv1_index;
                totalBlocks += lv2_index + 1;
                UInt32 preserve = GetBlocksToPreserve(totalBlocks);
                //Console.WriteLine("preserve block: {0} (+{1})", totalBlocks, preserve);

                if (preserve > 0)
                {
                    vfs.PreserveBlock(preserve);
                    data.blockPreserved += preserve;
                    //Console.WriteLine("data.blockPreserved = {0}", data.blockPreserved);
                    Save();
                }

                Block lv0_block = new Block(vfs, data.dataBlockId[lv0_index]);
                if (lv0_block.index == UInt32.MaxValue)
                {
                    lv0_block = vfs.AllocateBlock(0xFF);
                    data.dataBlockId[lv0_index] = lv0_block.index;
                    Save();
                }

                Block lv1_block = new Block(vfs, lv0_block.Read<UInt32>(lv1_index * sizeof(UInt32)));
                if (lv1_block.index == UInt32.MaxValue)
                {
                    lv1_block = vfs.AllocateBlock(0xFF);
                    lv0_block.Write(lv1_index * sizeof(UInt32), lv1_block.index);
                }

                Block lv2_block = new Block(vfs, lv1_block.Read<UInt32>(lv2_index * sizeof(UInt32)));
                if (lv2_block.index == UInt32.MaxValue)
                {
                    lv2_block = vfs.AllocateBlock(0xFF);
                    lv1_block.Write(lv2_index * sizeof(UInt32), lv2_block.index);
                }

                return lv2_block;
            }
        }

        /// <summary>
        /// 返回 inode 数据区大小
        /// </summary>
        /// <returns></returns>
        public UInt32 Size()
        {
            return data.sizeByte;
        }

        /// <summary>
        /// 读取所有数据
        /// </summary>
        /// <returns></returns>
        public byte[] Read()
        {
            UInt32 count = data.sizeByte;
            byte[] arr = new byte[count];
            Read(0, arr, count);
            return arr;
        }

        /// <summary>
        /// 读取数据
        /// </summary>
        /// <param name="position"></param>
        /// <param name="arr"></param>
        /// <param name="count"></param>
        public void Read(UInt32 position, byte[] arr, UInt32 count)
        {
            // 超出文件大小的读取，则忽略
            if (position >= data.sizeByte || count == 0)
            {
                return;
            }
            // 读取范围超出实际大小，则缩小
            if ((UInt64)position + count > data.sizeByte)
            {
                count = data.sizeByte - position;
            }

            UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
            UInt32 arrOffset = 0;

            // 分块读取
            do
            {
                Block block = GetBlockAtPosition(position);
                UInt32 offset = position % blockSize;
                UInt32 bytesToRead = Math.Min(count - arrOffset, blockSize - offset);

                if (block.index == UInt32.MaxValue)
                {
                    // block 并没有被分配：直接赋 0
                    for (var i = arrOffset; i < arrOffset + bytesToRead; ++i)
                    {
                        arr[i] = 0;
                    }
                }
                else
                {
                    // block 实际存在：读取数据
                    block.ReadArray(offset, arr, (int)arrOffset, (int)bytesToRead);
                }

                arrOffset += bytesToRead;
                position += (uint)bytesToRead;
            } while (arrOffset < arr.Length);
        }

        /// <summary>
        /// 写入数据作为 inode 数据，释放未使用的数据块
        /// </summary>
        /// <param name="arr"></param>
        public void Write(byte[] arr)
        {
            Resize((UInt32)arr.Length);
            Write(0, arr);
        }

        /// <summary>
        /// 写入数据
        /// </summary>
        /// <param name="position"></param>
        /// <param name="arr"></param>
        public void Write(UInt32 position, byte[] arr)
        {
            UInt32 blockSize = vfs.GetSuperBlock().data.blockSize;
            UInt32 arrOffset = 0;

            // 分块写入
            do
            {
                Block block = PrepareBlockAtPosition(position);
                UInt32 offset = position % blockSize;
                UInt32 bytesToWrite = Math.Min((UInt32)(arr.Length - arrOffset), blockSize - offset);

                block.WriteArray(offset, arr, (int)arrOffset, (int)bytesToWrite);

                arrOffset += bytesToWrite;
                position += (uint)bytesToWrite;
            } while (arrOffset < arr.Length);

            // 更新文件大小
            if (position > data.sizeByte)
            {
                data.sizeByte = position;
            }

            // 更新最后修改时间
            data.modifyTime = (UInt64)DateTime.Now.Ticks;
            Save();
        }

        /// <summary>
        /// 将 inode 数据写入存储介质
        /// </summary>
        /// <param name="vfs"></param>
        public void Save()
        {
            vfs.GetDevice().Write(GetPosition(), data);
        }

        private static WeakValueDictionary<UInt32, INode> inodeInstances = new WeakValueDictionary<uint, INode>();

        /// <summary>
        /// 从存储介质中获取 inode
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static INode Load(VFSCore vfs, UInt32 index)
        {
            if (index >= vfs.GetSuperBlock().data.inodeCapacity)
            {
                throw new Exception("无效 inode 编号");
            }

            INode inode = null;

            if (inodeInstances.ContainsKey(index))
            {
                inode = inodeInstances[index];
                return inode;
            }
            else
            {
                _INode data = vfs.GetDevice().Read<_INode>(GetPosition(vfs, index));
                inode = new INode(vfs, data, index);
                inodeInstances[index] = inode;
            }

            inode.data.accessTime = (UInt64)DateTime.Now.Ticks;
            inode.Save();
            return inode;
        }

        /// <summary>
        /// 创建一个新的 inode
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static INode Create(VFSCore vfs, UInt32 index, UInt32 flags, UInt32 owner)
        {
            if (index >= vfs.GetSuperBlock().data.inodeCapacity)
            {
                throw new Exception("无效 inode 编号");
            }

            _INode data = new _INode(flags, owner);
            INode inode = new INode(vfs, data, index);
            inode.Save();
            inodeInstances[index] = inode;

            return inode;
        }
    }
}
