using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VirtualFileSystem
{
    public partial class VFS
    {
        public enum FileMode
        {
            /// <summary>
            /// 如果文件已存在，则将引发 IO 异常。
            /// </summary>
            CreateNew = 1,

            /// <summary>
            /// 如果文件已存在，它将被覆盖。
            /// </summary>
            Create = 2,

            /// <summary>
            /// 指定操作系统应打开现有文件。如果该文件不存在，则引发文件未找到异常。
            /// </summary>
            Open = 3,

            /// <summary>
            /// 指定操作系统应打开文件（如果文件存在）；否则，应创建新文件。
            /// </summary>
            OpenOrCreate = 4,

            /// <summary>
            /// 指定操作系统应打开现有文件。文件一旦打开，就将被截断为零字节大小。
            /// </summary>
            Truncate = 5,

            /// <summary>
            /// 若存在文件，则打开该文件并查找到文件尾，或者创建一个新文件。
            /// </summary>
            Append = 6
        }

        public class File
        {
            public UInt32 position = 0;
            public String name;

            private VFSCore vfs;
            private INode inode;
            
            public File(VFSCore vfs, String path, FileMode fileMode)
            {
                this.vfs = vfs;

                var directory = VFS.GetPathDirectory(path);
                var name = VFS.GetPathName(path);
                VFS.AssertNameValid(name);

                INodeDirectory dir = INodeDirectory.Resolve(vfs, directory);
                if (dir == null)
                {
                    throw new Exception("无法访问目录");
                }
                switch (fileMode)
                {
                    case FileMode.CreateNew:
                        if (dir.Contains(name))
                        {
                            throw new Exception("文件已存在");
                        }
                        CreateFile(dir, name);
                        break;
                    case FileMode.Create:
                        if (dir.Contains(name))
                        {
                            OpenFile(dir, name);
                            inode.Resize(0);
                        }
                        else
                        {
                            CreateFile(dir, name);
                        }
                        break;
                    case FileMode.Open:
                        if (!dir.Contains(name))
                        {
                            throw new Exception("文件未找到");
                        }
                        OpenFile(dir, name);
                        break;
                    case FileMode.OpenOrCreate:
                        if (dir.Contains(name))
                        {
                            OpenFile(dir, name);
                        }
                        else
                        {
                            CreateFile(dir, name);
                        }
                        break;
                    case FileMode.Truncate:
                        if (!dir.Contains(name))
                        {
                            throw new Exception("文件未找到");
                        }
                        OpenFile(dir, name);
                        inode.Resize(0);
                        break;
                    case FileMode.Append:
                        if (!dir.Contains(name))
                        {
                            CreateFile(dir, name);
                        }
                        else
                        {
                            OpenFile(dir, name);
                            position = inode.data.sizeByte;
                        }
                        break;
                    default:
                        throw new ArgumentException();
                }
            }

            /// <summary>
            /// 返回文件大小
            /// </summary>
            /// <returns></returns>
            public UInt32 Size()
            {
                return inode.data.sizeByte;
            }

            /// <summary>
            /// 通过创建一个新文件来初始化该类
            /// </summary>
            /// <param name="vfs"></param>
            /// <param name="name"></param>
            /// <param name="dir"></param>
            private void CreateFile(INodeDirectory dir, String name)
            {
                inode = vfs.AllocateINode(0, 2333);
                dir.Add(name, inode);
            }

            /// <summary>
            /// 通过打开现有文件来初始化该类
            /// </summary>
            /// <param name="dir"></param>
            /// <param name="name"></param>
            private void OpenFile(INodeDirectory dir, String name)
            {
                inode = INode.Load(vfs, dir.Find(name));
            }
            
            /// <summary>
            /// 移动文件指针
            /// </summary>
            /// <param name="position"></param>
            public void Seek(UInt32 position)
            {
                this.position = position;
            }

            /// <summary>
            /// 写入字节数据
            /// </summary>
            /// <param name="array"></param>
            public void Write(byte[] array)
            {
                Write(array, 0, (uint)array.Length);
            }
            
            /// <summary>
            /// 写入字节数据
            /// </summary>
            /// <param name="array"></param>
            /// <param name="offset"></param>
            /// <param name="count"></param>
            public void Write(byte[] array, UInt32 offset, UInt32 count)
            {
                byte[] arr = new byte[count];
                Buffer.BlockCopy(array, (int)offset, arr, 0, (int)count);
                inode.Write(position, arr);
                position += count;
            }

            /// <summary>
            /// 读取从当前位置开始所有数据
            /// </summary>
            /// <param name="array"></param>
            /// <returns></returns>
            public UInt32 Read(byte[] array)
            {
                if (position >= inode.data.sizeByte)
                {
                    return 0;
                }
                var count = inode.data.sizeByte - position;
                inode.Read(position, array, count);
                position += count;
                return count;
            }

            /// <summary>
            /// 读取字节数据
            /// </summary>
            /// <param name="array"></param>
            /// <param name="offset"></param>
            /// <param name="count"></param>
            /// <returns></returns>
            public UInt32 Read(byte[] array, UInt32 offset, UInt32 count)
            {
                if (position >= inode.data.sizeByte)
                {
                    return 0;
                }
                if (position + count > inode.data.sizeByte)
                {
                    count = inode.data.sizeByte - position;
                }
                byte[] arr = new byte[count];
                inode.Read(position, arr, count);
                Buffer.BlockCopy(arr, 0, array, (int)offset, (int)count);
                position += count;
                return count;
            }
        }
    }
}
