using System;
using VirtualFileSystem;
using System.Drawing;

namespace Explorer
{
    public class DirectoryItem
    {
        public String size { get; set; }

        public String name { get; set; }

        public String path { get; set; }

        public String extension { get; set; }

        public DateTime modifyTime { get; set; }

        public DateTime creationTime { get; set; }

        public Bitmap icon { get; set; }

        public Boolean isDirectory { get; set; }

        public UInt32 flag { get; set; }

        public UInt32 owner { get; set; }

        public UInt32 inodeIndex { get; set; }

        public UInt32 blockPreserved { get; set; }

        public UInt32 refCount { get; set; }

        public DirectoryItem(VFS.DirectoryInfo info)
        {
            this.modifyTime = new DateTime((long)info.modifyTime);
            this.creationTime = new DateTime((long)info.creationTime);
            this.name = info.name;
            this.path = info.path;
            this.isDirectory = info.isDirectory;
            this.flag = info.flags;
            this.owner = info.owner;
            this.inodeIndex = info.inodeIndex;
            this.blockPreserved = info.inode.blockPreserved;
            this.refCount = info.inode.linkCount;
            
            if (info.isDirectory)
            {
                this.extension = "文件夹";
                this.icon = ShellFileInfo.GetFolderIcon(ShellFileInfo.IconSize.Small, ShellFileInfo.FolderType.Closed);
                this.size = "";
            }
            else
            {
                this.extension = ShellFileInfo.GetFileTypeDescription(this.name);
                this.icon = ShellFileInfo.GetFileIcon(this.name, ShellFileInfo.IconSize.Small, false);
                this.size = Utils.FormatSize(info.size);
            }
        }
    }
}
