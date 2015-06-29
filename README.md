# VFS

## 综述

本项目使用 C# 实现了一个虚拟文件系统，并基于该虚拟文件系统接口，使用 WPF 技术开发了操作界面。

### 文件系统功能特性
- 索引文件系统，可在格式化时调整索引节点数量和区块大小
- 使用位图进行空间管理和索引节点管理（一次性检查 32 位）
- 真实的空间管理，虚拟文件的写入和读取操作可对应到实际虚拟磁盘文件物理地址
- 使用红黑树进行目录项管理（Dictionary 集合）
- 支持任意长度文件名和任意深度路径
- 数据区块支持二级查表，单个文件最大可达 2GB（Int32 限制）
- 最大支持总存储空间 1.5GB（内存映射文件限制）
- 支持文件随机访问和读取，也支持顺序访问和读取
- 支持 Seek，可以快速创建很大的空文件
- 懒惰分配数据区块，即只有在需要对数据区块写入时，才会查找并分配区块
- 底层使用内存映射文件存储，接口上支持纯内存存储（并没有实现）
- 所有写入操作都会立即持久化
- 位图存储在内存中，不需要从虚拟磁盘文件上一遍遍读取
- 支持硬链接
- 面向对象的设计
- 提供了与操作系统文件操作 API 极其相似的应用程序接口
- API 与底层实现分离，调用者不需要了解底层相关信息（如数据区块等）

### 操作界面功能特性
- 基于 WPF，界面美观，接近 Windows 10 资源管理器
- 支持从本地磁盘添加文件
- 支持使用操作系统程序打开读取虚拟文件系统中的文件
- 支持使用操作系统程序编辑文件并自动写回虚拟文件系统

### 由于时间关系，该项目还有很多可以改进的地方

- 没有为内存映射文件存储编写内存缓存，因此其执行效率远远比不上纯内存存储
- 没有为顺序写入或顺序读取优化，而是直接将这些操作转化为了对各块的随机读取和写入，因此顺序写入和读取的效率很低
- 没有分区域按需进行内存映射，而是直接映射了整个虚拟磁盘文件，因此运行时可能会占用较多共享内存空间，甚至可能由于内存不足而无法启动
- 目录项结构没有使用 B 树或 B+ 树存储，且由于目录结构建立在序列化之上，因此每次修改目录都需要重写所有其占用的区块
- 目前查询空闲空间时，当检查到 32 位区块有空闲空间后，最多还需要 31 次比较操作才能获得空闲编号，该操作可进一步利用二分查找优化
- 空闲空间管理可以由位图优化为更高级的数据结构，来加速查询
- 没有编写复制、粘贴、创建链接的图形化界面

## 代码说明
bin/Explorer.exe 是该项目虚拟文件系统操作界面（文件管理器）。您至少需要 .net Framework 4 才可以运行它。它目标平台为 x86，因此 x86 和 x64 操作系统上都可以运行。推荐在 Windows 7 及以上操作系统中使用。

src/ 下是 VS 2015 解决方案。该项目由 C# 写成。由于文件管理器部分界面使用了第三方库，因此您可能并不能直接编译该项目。请参阅本文末查看解决方案。

## 具体实现

### 1. VFS

VFS(Virtual File System) 实现了一套界面无关的虚拟文件系统接口。主要由以下几层组成：

#### 1.1 DeviceAdapter/AbstractDevice

存储介质，即该文件系统建立在什么样的存储介质之上。AbstractDevice 是一个抽象类，需要实现如下接口：

`Size()`：获取该存储介质总共的可用空间大小
`Read<T>(long position)`：读取该介质 position 处，长度为 sizeof(T) 的数据到值类型 T 并返回
`ReadArray<T>(long position, int count)`：读取该介质 position 处，长度为 sizeof(T)*count 的数据
`Write<T>(long position, T structure)`：将值类型 T 写入该介质 position 处，共写入 sizeof(T) 字节
`WriteArray<T>(long position, T[] array, int offset, int count)`：写入 T[]，不在赘述

基于 AbstractDevice，内置实现了 FileAdapter，即物理磁盘上的虚拟磁盘文件作为 VFS 存储介质。

#### 1.2 VFSCore

虚拟文件系统对象。它需要建立在一个存储介质之上，提供了综合性函数，如格式化磁盘等。它对 inode 和数据区块位图进行缓存，包含以下成员：

```c#
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
```

存储介质上空间排布由低到高如下：

`_SuperBlock | inodeBitmapVector[] | _INode[] | blockBitmapVector[] | Block[]`

注，各个结构之间并没有按照指定大小对齐，它们都是紧密排列的。如，`_SuperBlock` 地址位于 `0`，则 `inodeBitmapVector[]` 位于 `0+sizeof(_SuperBlock)`，`_Inode[]` 位于 `0+sizeof(_SuperBlock)+4*count(inodeBitmapVector[])，blockBitmapVector[]` 位于`0+sizeof(_SuperBlock)+4*count(inodeBitmapVector[])+sizeof(_INode)*count(_Inode[])` 等等。另外，`Block[]` 之后还会有一小部分没有被使用的空间（由分配算法产生）。

#### 1.3 SuperBlock

超级块操作对象，存储了该文件系统格式化后的参数。`SuperBlock` 包含 `_SuperBlock` 结构体，`_SuperBlock` 结构体包含了这些关键数据，并且会被写入到存储介质中。`_SuperBlock` 结构体总是会被写入到存储介质中的 `0` 号位置。

`_SuperBlock` 包含：

```c#
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
```

`_SuperBlock` 最后一个字段是 `magicValue`，其在存储介质上的位置是 `4+4+2+4+4+4` 字节处。VFS 使用该数据判断这个文件系统是否有效，如果无效，则会要求格式化。（创建空磁盘介质时，该位置数据是 `0`，因此会要求格式化）

`SuperBlock` 是对 `_SuperBlock` 结构体的封装，提供了一些便捷的预计算值（显然，这些数据可以由超级块结构体所包含的数据计算出来），方便上层代码计算：

```c#
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
```

#### 1.4 Block

数据区块操作对象，构造时需要提供“存储介质”对象和所要操作的数据区块编号。一个数据区块可容纳的数据量在格式化时确定。该对象并不存储区块中的具体内容，相反，它提供了读取和访问的封装函数，对存储介质进行读取和访问。

事实上，该对象提供的是对某个特定编号区块特定位置进行读写操作的接口。这样，上层代码无需关心需要向数据区块写入数据在存储介质上的具体位置。这些计算操作由区块操作对象完成。

#### 1.5 INode

`INode` 操作对象。`INode` 包含 `_INode` 结构体，`_INode` 结构体会被持久化到磁盘介质中。`INode` 操作对象可以从存储介质上创建（载入），也可以新分配。

`INode` 操作对象包含一个 `WeakValueDictionary` 弱引用集合静态成员。当 `INode` 操作对象从存储介质中载入时，对于相同的 inode 编号将会获取到相同的 `INode` 操作对象实例，这样，对 `_INode` 结构体的修改不会导致不一致。弱引用集合使得这些操作对象的内存可以被操作系统回收，但又可以保持同样编号总是引用到相同的实例。

`_INode` 结构体包含：

```c#
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
```

`INode` 包含：

```c#
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
```

#### 1.6 INodeDirectory

`INodeDirectory` 是对目录项操作包装类。inode 要么是一个文件，要么是一个目录。当 inode 是一个目录时，可以使用 `INodeDirectory` 操作这个 inode 的内容（数据区块将被解读为目录结构）。

`INodeDirectory` 使用 `Dictionary` 存储目录项，使用 protobuf 在 inode 上层进行压缩存储的的序列化与反序列化。

#### 1.7 VFS

VFS 是面向应用程序的文件系统操作类（VFS API），提供诸如格式化、创建文件操作类（`VFS.File`）、创建目录操作类（`VFS.Directory`）等接口。构造 VFS 时，调用者需要传入一个磁盘介质（`AbstractDevice`）。

#### 1.8 VFS.File

`VFS.File` 是面向应用程序的文件操作类（File API），提供诸如根据路径打开文件、移动文件指针、向指针处写入数据、从指针处读取数据等接口。

#### 1.9 VFS.Directory

`VFS.File` 是面向应用程序的目录操作类（Directory API），提供诸如创建新文件夹、查询文件夹内容、重命名或删除一个目录项等功能。

### 2. Explorer

文件浏览器部分使用 WPF 技术实现，使用了 infragistics WPF 库帮助进行界面绘制。也正是由于这个原因，如果您需要自行编译文件浏览器部分代码，请先在本机安装 infragistics WPF 库。

infragistics 是商业软件。您可以从该地址获得一份非法拷贝：http://www.cnblogs.com/zeroone/p/4540900.html
