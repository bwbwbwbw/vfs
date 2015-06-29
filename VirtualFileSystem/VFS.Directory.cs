using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VirtualFileSystem
{
    public partial class VFS
    {
        public class DirectoryInfo
        {
            public Boolean isDirectory { get; set; }

            public String name { get; set; }

            public String path { get; set; }

            public UInt32 size { get; set; }

            public UInt64 accessTime { get; set; }

            public UInt64 modifyTime { get; set; }

            public UInt64 creationTime { get; set; }

            public UInt32 flags { get; set; }

            public UInt32 owner { get; set; }

            public _INode inode { get; set; }

            public UInt32 inodeIndex { get; set; }
        }

        public class Directory
        {
            private VFSCore vfs;
            private INodeDirectory dir;

            private String path;

            public Directory(VFSCore vfs, String path)
            {
                this.vfs = vfs;

                if (!path.EndsWith("/"))
                {
                    path += "/";
                }

                this.path = path;

                dir = INodeDirectory.Resolve(vfs, path);

                if (dir == null)
                {
                    throw new Exception("无效路径");
                }
            }
            
            /// <summary>
            /// 创建一个文件夹
            /// </summary>
            /// <param name="name"></param>
            public void CreateDirectory(String name)
            {
                VFS.AssertNameValid(name);

                INode inode = vfs.AllocateINode(1, 2333);
                if (!dir.Add(name, new INodeDirectory(vfs, inode)))
                {
                    throw new Exception("创建文件夹失败");
                }
            }

            /// <summary>
            /// 目录下是否包含一个文件或目录
            /// </summary>
            /// <param name="name"></param>
            /// <returns></returns>
            public Boolean Contains(String name)
            {
                return dir.Contains(name);
            }

            /// <summary>
            /// 列出一个目录下所有内容
            /// </summary>
            /// <returns></returns>
            public List<DirectoryInfo> List()
            {
                var ret = new List<DirectoryInfo>();
                var entries = dir.List();
                foreach (var entry in entries)
                {
                    var info = new DirectoryInfo();
                    INode inode = INode.Load(vfs, entry.Value);
                    info.isDirectory = inode.IsDirectory();
                    info.accessTime = inode.data.accessTime;
                    info.creationTime = inode.data.creationTime;
                    info.flags = inode.data.flags;
                    info.modifyTime = inode.data.modifyTime;
                    info.name = entry.Key;
                    info.path = this.path + entry.Key;
                    info.owner = inode.data.owner;
                    info.size = inode.data.sizeByte;
                    info.inode = inode.data;
                    info.inodeIndex = inode.index;
                    ret.Add(info);
                }
                return ret;
            }

            /// <summary>
            /// 重命名文件或文件夹
            /// </summary>
            /// <param name="oldName"></param>
            /// <param name="newName"></param>
            public void Rename(String oldName, String newName)
            {
                VFS.AssertNameValid(oldName);
                VFS.AssertNameValid(newName);

                if (!dir.Contains(oldName))
                {
                    throw new Exception("文件或文件夹未找到");
                }
                if (dir.Contains(newName))
                {
                    throw new Exception("新文件名与现有文件或文件夹名称冲突");
                }
                if (!dir.Rename(oldName, newName))
                {
                    throw new Exception("重命名失败");
                }
            }
            
            /// <summary>
            /// 删除文件或文件夹
            /// </summary>
            /// <param name="name"></param>
            public void Delete(String name)
            {
                if (!dir.Contains(name))
                {
                    throw new Exception("文件或文件夹未找到");
                }
                if (!dir.Delete(name))
                {
                    throw new Exception("删除失败");
                }
            }
        }
    }
}
