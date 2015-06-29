using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ProtoBuf;

namespace VirtualFileSystem
{
    class INodeDirectory
    {
        private VFSCore vfs;

        /// <summary>
        /// 目录关联的 inode 节点
        /// </summary>
        private INode inode;

        /// <summary>
        /// 目录项
        /// </summary>
        private Dictionary<String, UInt32> entries;
        
        public INodeDirectory(VFSCore vfs, INode inode)
        {
            this.vfs = vfs;
            this.inode = inode;
            this.entries = new Dictionary<String, UInt32>();
        }

        /// <summary>
        /// 包含目录项个数
        /// </summary>
        /// <returns></returns>
        public int Size()
        {
            return entries.Count;
        }

        /// <summary>
        /// 返回是否包含目录项
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Boolean Contains(String name)
        {
            return entries.ContainsKey(name);
        }

        /// <summary>
        /// 查找目录项的 inode
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public UInt32 Find(String name)
        {
            return entries[name];
        }

        /// <summary>
        /// 持久化该目录
        /// </summary>
        private void Save()
        {
            MemoryStream ms = new MemoryStream();
            Serializer.Serialize(ms, entries);
            byte[] bytes = ms.ToArray();
            inode.Write(bytes);
        }

        /// <summary>
        /// 从存储介质上载入该目录
        /// </summary>
        public void Load()
        {
            byte[] bytes = inode.Read();
            MemoryStream ms = new MemoryStream(bytes);
            entries = Serializer.Deserialize<Dictionary<String, UInt32>>(ms);
        }

        /// <summary>
        /// 添加本目录
        /// </summary>
        /// <param name="inodeIndex"></param>
        /// <returns></returns>
        private Boolean AddSelf()
        {
            if (Contains("."))
            {
                return false;
            }

            entries["."] = inode.index;
            Save();

            return true;
        }

        /// <summary>
        /// 添加父目录
        /// </summary>
        /// <param name="inodeIndex"></param>
        /// <returns></returns>
        private Boolean AddParent(UInt32 inodeIndex)
        {
            if (Contains(".."))
            {
                return false;
            }

            entries[".."] = inodeIndex;
            Save();

            return true;
        }

        /// <summary>
        /// 添加一个文件目录项
        /// </summary>
        /// <param name="name"></param>
        /// <param name="inodeIndex"></param>
        /// <returns></returns>
        public Boolean Add(String name, INode inode)
        {
            if (Contains(name))
            {
                return false;
            }

            entries[name] = inode.index;
            inode.data.linkCount++;
            inode.Save();
            
            Save();
            return true;
        }

        /// <summary>
        /// 添加一个目录目录项
        /// </summary>
        /// <param name="name"></param>
        /// <param name="dir"></param>
        /// <returns></returns>
        public Boolean Add(String name, INodeDirectory dir)
        {
            if (Contains(name))
            {
                return false;
            }
            if (dir.Contains(".."))
            {
                return false;
            }

            entries[name] = dir.inode.index;
            dir.inode.data.linkCount++;
            dir.inode.Save();
            dir.AddParent(inode.index);

            Save();
            return true;
        }

        /// <summary>
        /// 重命名一个目录项
        /// </summary>
        /// <param name="oldName"></param>
        /// <param name="newName"></param>
        /// <returns></returns>
        public Boolean Rename(String oldName, String newName)
        {
            if (!Contains(oldName))
            {
                return false;
            }
            if (oldName == "." || oldName == "..")
            {
                return false;
            }
            if (Contains(newName))
            {
                return false;
            }
            var inodeIndex = entries[oldName];
            entries.Remove(oldName);
            entries[newName] = inodeIndex;
            Save();
            return true;
        }

        /// <summary>
        /// 删除一个目录项
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public Boolean Delete(String name)
        {
            if (!Contains(name))
            {
                return false;
            }
            if (name == "." || name == "..")
            {
                return false;
            }

            var inodeIndex = entries[name];

            INode inode = INode.Load(vfs, inodeIndex);
            if (inode.IsDirectory())
            {
                // 删除非空目录项:递归删除
                INodeDirectory id = INodeDirectory.Load(vfs, inodeIndex);
                if (id.Size() > 2)
                {
                    var l = id.List();
                    foreach (var pair in l)
                    {
                        id.Delete(pair.Key);
                    }
                }
            }
            
            inode.data.linkCount--;
            
            if (inode.data.linkCount == 0)
            {
                inode.Resize(0);
                vfs.DeAllocateINode(inode.index);
            }
            else
            {
                inode.Save();
            }
            
            entries.Remove(name);
            Save();

            return true;
        }

        /// <summary>
        /// 列出所有目录项
        /// </summary>
        /// <returns></returns>
        public List<KeyValuePair<String, UInt32>> List()
        {
            return entries.ToList();
        }

        /// <summary>
        /// 创建一个新目录
        /// </summary>
        /// <param name="vfs"></param>
        /// <returns></returns>
        public static INodeDirectory Create(VFSCore vfs)
        {
            INode inode = vfs.AllocateINode(1, 2333);
            INodeDirectory t = new INodeDirectory(vfs, inode);
            t.AddSelf();
            return t;
        }

        /// <summary>
        /// 根据 inode index 建立 INodeDirectory
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="inodeIndex"></param>
        /// <returns></returns>
        public static INodeDirectory Load(VFSCore vfs, UInt32 inodeIndex)
        {
            INode inode = INode.Load(vfs, inodeIndex);
            return Load(vfs, inode);
        }

        /// <summary>
        /// 根据 inode 建立 INodeDirectory
        /// </summary>
        /// <param name="vfs"></param>
        /// <param name="inode"></param>
        /// <returns></returns>
        public static INodeDirectory Load(VFSCore vfs, INode inode)
        {
            INodeDirectory t = new INodeDirectory(vfs, inode);
            t.Load();
            return t;
        }
        
        /// <summary>
        /// 根据路径解析目录，路径必须以 / 结尾
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static INodeDirectory Resolve(VFSCore vfs, String path)
        {
            INodeDirectory root = Load(vfs, 0);

            var pathCom = path.Split('/');
            var node = root;
            
            for (var i = 1; i < pathCom.Length - 1; ++i)
            {
                if (node.Contains(pathCom[i]) && INode.Load(vfs, node.Find(pathCom[i])).IsDirectory())
                {
                    node = Load(vfs, node.Find(pathCom[i]));
                }
                else
                {
                    return null;
                }
            }

            return node;
        }
    }
}
