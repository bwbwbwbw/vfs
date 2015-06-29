using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VirtualFileSystem.DeviceAdapter;

namespace VirtualFileSystem
{
    public partial class VFS
    {
        public VFSCore vfs;

        public VFS(AbstractDevice device)
        {
            vfs = new VFSCore(device);
        }

        public _SuperBlock GetSuperBlock()
        {
            return vfs.GetSuperBlock().data;
        }

        public DeviceAdapter.AbstractDevice GetDevice()
        {
            return vfs.GetDevice();
        }

        public Directory NewDirectory(String path)
        {
            return new Directory(vfs, path);
        }

        public File NewFile(String path, FileMode fileMode)
        {
            return new File(vfs, path, fileMode);
        }

        /// <summary>
        /// 是否已格式化
        /// </summary>
        /// <returns></returns>
        public Boolean IsFormated()
        {
            return vfs.IsFormated();
        }

        /// <summary>
        /// 格式化磁盘
        /// </summary>
        /// <param name="inodeCapacity"></param>
        /// <param name="blockSizeKB"></param>
        public void Format(UInt32 inodeCapacity, UInt16 blockSizeKB)
        {
            vfs.Format(inodeCapacity, blockSizeKB);
        }

        /// <summary>
        /// 判断一个路径是否有效
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static Boolean IsPathValid(String path)
        {
            if (path.Length == 0)
            {
                return false;
            }
            if (path.ElementAt(0) != '/')
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 判断一个文件名是否有效
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public static Boolean IsNameValid(String name)
        {
            if (name.Length == 0)
            {
                return false;
            }
            if (name.Contains('/'))
            {
                return false;
            }
            if (name == "." || name == "..")
            {
                return false;
            }
            if (name.EndsWith("."))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 文件名无效时抛出异常
        /// </summary>
        /// <param name="name"></param>
        public static void AssertNameValid(String name)
        {
            if (!IsNameValid(name))
            {
                throw new Exception("无效文件名");
            }
        }

        /// <summary>
        /// 路径无效时抛出异常
        /// </summary>
        /// <param name="path"></param>
        public static void AssertPathValid(String path)
        {
            if (!IsPathValid(path))
            {
                throw new Exception("无效路径");
            }
        }

        /// <summary>
        /// 获取一个路径的文件名
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static String GetPathName(String path)
        {
            AssertPathValid(path);
            var pos = path.LastIndexOf('/');
            return path.Substring(pos + 1);
        }

        /// <summary>
        /// 获取一个路径的目录
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static String GetPathDirectory(String path)
        {
            AssertPathValid(path);
            var pos = path.LastIndexOf('/');
            return path.Substring(0, pos + 1);
        }
    }
}
