using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using VirtualFileSystem;
using VirtualFileSystem.DeviceAdapter;
using System.Configuration;
using Infragistics.Controls.Interactions;
using System.Windows.Media.Effects;

namespace Explorer
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        AbstractDevice device;
        VFS vfs;

        String currentDirectory = "/";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void Reload()
        {
            LoadDirectory(currentDirectory);
            UpdateInfo();
        }

        private void GoUp()
        {
            if (currentDirectory.EndsWith("/") && currentDirectory.Length > 1)
            {
                currentDirectory = currentDirectory.Substring(0, currentDirectory.Length - 1);
            }
            var pos = currentDirectory.LastIndexOf("/");
            currentDirectory = currentDirectory.Substring(0, pos + 1);
            if (LoadDirectory(currentDirectory))
            {
                history.Add(currentDirectory);
                historyNeedle++;
                buttonForward.IsEnabled = (historyNeedle + 1 < history.Count);
                buttonBack.IsEnabled = (historyNeedle - 1 >= 0);
            }
        }

        private Boolean LoadDirectory(String directory)
        {
            if (!directory.StartsWith("/"))
            {
                System.Windows.Forms.MessageBox.Show("您输入的路径无效！", "VFS", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return false;
            }
            if (!directory.EndsWith("/"))
            {
                directory += "/";
            }
            currentDirectory = directory;
            textboxPath.Text = directory;

            try
            {
                var dir = vfs.NewDirectory(directory);
                var list = dir.List();
                listView.Items.Clear();
                foreach (var info in list)
                {
                    listView.Items.Add(new DirectoryItem(info));
                }
            }
            catch (Exception e)
            {
                System.Windows.Forms.MessageBox.Show(e.Message, "VFS", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        private void UpdateInfo()
        {
            labelDeviceSize.Caption = Utils.FormatSize(vfs.GetDevice().Size());
            labelBlockSize.Caption = Utils.FormatSize(vfs.GetSuperBlock().blockSize);
            labelINodeCapacity.Caption = vfs.GetSuperBlock().inodeCapacity.ToString();
            labelINodeAllocated.Caption = vfs.GetSuperBlock().inodeAllocated.ToString();
            labelBlockCapacity.Caption = vfs.GetSuperBlock().blockCapacity.ToString();
            labelBlockAllocated.Caption = vfs.GetSuperBlock().blockAllocated.ToString();
            labelBlockPreserved.Caption = vfs.GetSuperBlock().blockPreserved.ToString();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (Properties.Settings.Default["vfsfile"] == null || ((String)(Properties.Settings.Default["vfsfile"])).Length == 0)
            {
                var r = System.Windows.Forms.MessageBox.Show("首次启动 VFS 需要在磁盘上建立一个虚拟磁盘镜像文件，镜像文件大小为 1GB。\n点击确定后请选择一个可以写入的位置来建立此文件，点击取消关闭程序。", "磁盘镜像文件未找到", System.Windows.Forms.MessageBoxButtons.OKCancel, System.Windows.Forms.MessageBoxIcon.Information);
                if (r == System.Windows.Forms.DialogResult.Cancel)
                {
                    Close();
                    return;
                }

                using (var dialog = new System.Windows.Forms.SaveFileDialog())
                {
                    dialog.Title = "保存磁盘镜像文件";
                    dialog.FileName = "vfs.bin";
                    var result = dialog.ShowDialog();
                    if (result == System.Windows.Forms.DialogResult.Cancel)
                    {
                        Close();
                        return;
                    }
                    else
                    {
                        try
                        {
                            device = new FileAdapter(dialog.FileName, 1 << 10 << 10 << 10);
                        }
                        catch (Exception ex)
                        {
                            System.Windows.Forms.MessageBox.Show("打开镜像文件失败，可能您没有权限在该位置写入文件，或您内存不足。程序即将退出，请重新打开程序。");
                            Close();
                            return;
                        }
                        // 更新配置
                        Properties.Settings.Default["vfsfile"] = dialog.FileName;
                        Properties.Settings.Default.Save();
                    }
                }
            }
            else
            {
                device = new FileAdapter((String)Properties.Settings.Default["vfsfile"], 1 << 10 << 10 << 10);
            }

            vfs = new VFS(device);
            if (!vfs.IsFormated())
            {
                var result = System.Windows.Forms.MessageBox.Show("该磁盘镜像文件尚未格式化，是否格式化?", "VFS", System.Windows.Forms.MessageBoxButtons.YesNo);
                if (result == System.Windows.Forms.DialogResult.No)
                {
                    Close();
                    return;
                }

                vfs.Format(device.Size() >> 10, 4);

                // 写入一个示例文件
                var file = vfs.NewFile("/README.txt", VFS.FileMode.Create);
                file.Write(Encoding.UTF8.GetBytes("Hello world！\r\n\r\n这个文件是格式化时自动建立的，如果你看到了这个文件，说明一切工作正常。\r\n\r\n当你使用记事本修改这个文件并保存以后，它会同步到虚拟文件系统中。\r\n\r\n该虚拟文件系统是索引文件系统，inode 默认占总空间的 10%，理论可支持单一文件最大 2GB。"));

                System.Windows.Forms.MessageBox.Show("磁盘格式化成功！", "VFS", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
            }

            LoadDirectory("/");

            UpdateInfo();
        }

        private void ButtonRefresh_Click(object sender, RoutedEventArgs e)
        {
            UpdateInfo();
        }

        private XamDialogWindow CreateDialogWindow(double height = 100)
        {
            return new XamDialogWindow() {
                Width = 350, Height = height,
                StartupPosition = StartupPosition.Center,
                CloseButtonVisibility = Visibility.Collapsed
            };
        }

        private void listView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            const int bufferSize = 4096;

            var item = (DirectoryItem)listView.SelectedItem;

            if (item == null)
            {
                return;
            }

            if (item.isDirectory)
            {
                if (item.name == ".")
                {
                    Reload();
                    return;
                }
                if (item.name == "..")
                {
                    GoUp();
                    return;
                }

                if (history.Count - historyNeedle - 1 > 0)
                {
                    history.RemoveRange(historyNeedle + 1, history.Count - historyNeedle - 1);
                }
                var newDir = currentDirectory + item.name + "/";
                if (LoadDirectory(newDir))
                {
                    history.Add(newDir);
                    historyNeedle++;
                    buttonForward.IsEnabled = (historyNeedle + 1 < history.Count);
                    buttonBack.IsEnabled = (historyNeedle - 1 >= 0);
                }
            }
            else
            {
                // 读取文件内容到临时变量
                String tempFileName = System.IO.Path.GetTempFileName() + System.IO.Path.GetExtension(item.path);
                using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(new System.IO.FileStream(tempFileName, System.IO.FileMode.Create)))
                {
                    var file = vfs.NewFile(item.path, VFS.FileMode.Open);
                    byte[] buffer = new byte[bufferSize];
                    int count;
                    while ((count = (int)file.Read(buffer, 0, (uint)buffer.Length)) != 0)
                    {
                        writer.Write(buffer, 0, count);
                    }
                }

                FadeOutWindow();

                XamDialogWindow win = CreateDialogWindow();
                win.Header = "VFS";

                Label label = new Label();
                label.Content = "正在等待应用程序关闭, 文件内容将在程序关闭后自动更新...";
                label.VerticalContentAlignment = VerticalAlignment.Center;
                label.HorizontalAlignment = HorizontalAlignment.Center;

                win.Content = label;
                win.IsModal = true;
                windowContainer.Children.Add(win);

                Utils.ProcessUITasks();

                // 调用系统默认程序打开
                var process = new System.Diagnostics.Process();
                process.StartInfo = new System.Diagnostics.ProcessStartInfo(tempFileName);
                process.EnableRaisingEvents = true;
                process.Start();

                try
                {
                    process.WaitForExit();
                }
                catch (Exception ex)
                {
                }

                // 在关闭后，读取内容写回文件系统
                using (System.IO.BinaryReader reader = new System.IO.BinaryReader(new System.IO.FileStream(tempFileName, System.IO.FileMode.Open)))
                {
                    var file = vfs.NewFile(item.path, VFS.FileMode.Create);
                    byte[] buffer = new byte[bufferSize];
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        file.Write(buffer, 0, (uint)count);
                    }
                }

                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();

                Reload();
                UpdateInfo();
            }
        }

        private void FadeOutWindow(Boolean blur = true)
        {
            if (blur)
            {
                var blurEffect = new BlurEffect() { Radius = 10 };
                gridContent.Effect = blurEffect;
            }
            gridContent.Opacity = 0.5;
        }

        private void FadeInWindow()
        {
            gridContent.Opacity = 1;
            gridContent.Effect = null;
        }

        private void buttonUploadLocal_Click(object sender, RoutedEventArgs e)
        {
            const int bufferSize = 4096;

            var dialog = new System.Windows.Forms.OpenFileDialog();
            dialog.Title = "选择文件";
            var result = dialog.ShowDialog();
            if (result == System.Windows.Forms.DialogResult.Cancel)
            {
                return;
            }

            VFS.Directory dir = vfs.NewDirectory(currentDirectory);
            if (dir.Contains(dialog.SafeFileName))
            {
                System.Windows.Forms.MessageBox.Show("文件夹下已存在同名文件或文件夹", "添加失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            try
            {
                // 写入文件
                using (var reader = new System.IO.BinaryReader(new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Open)))
                {
                    var file = vfs.NewFile(currentDirectory + dialog.SafeFileName, VFS.FileMode.Create);
                    byte[] buffer = new byte[bufferSize];
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
                    {
                        file.Write(buffer, 0, (uint)count);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show("添加文件失败 :-(", "添加失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            Reload();
        }

        private void buttonNewFolder_Click(object sender, RoutedEventArgs e)
        {
            XamDialogWindow win = CreateDialogWindow(150);
            win.Header = "创建文件夹";
            
            StackPanel stack = new StackPanel();
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Margin = new Thickness(10);

            Label label = new Label();
            label.Content = "请输入文件夹名字";
            stack.Children.Add(label);

            TextBox txtName = new TextBox();
            txtName.Text = "新建文件夹";
            stack.Children.Add(txtName);

            StackPanel stackButton = new StackPanel();
            stackButton.Orientation = Orientation.Horizontal;
            stackButton.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(stackButton);

            Button btnOK = new Button();
            btnOK.Content = "创建";
            btnOK.Padding = new Thickness(10, 0, 10, 0);
            btnOK.Margin = new Thickness(10);
            stackButton.Children.Add(btnOK);
            Button btnCancel = new Button();
            btnCancel.Content = "取消";
            btnCancel.Padding = new Thickness(10, 0, 10, 0);
            btnCancel.Margin = new Thickness(10);
            stackButton.Children.Add(btnCancel);

            FadeOutWindow();

            win.Content = stack;
            win.IsModal = true;
            windowContainer.Children.Add(win);

            txtName.Focus();

            btnOK.Click += delegate
            {
                VFS.Directory dir = vfs.NewDirectory(currentDirectory);
                if (dir.Contains(txtName.Text)) {
                    System.Windows.Forms.MessageBox.Show("该文件或文件夹已存在", "创建失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                try
                {
                    dir.CreateDirectory(txtName.Text);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "创建失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
                Reload();
            };

            btnCancel.Click += delegate
            {
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
            };
        }

        private void buttonNewFile_Click(object sender, RoutedEventArgs e)
        {
            XamDialogWindow win = CreateDialogWindow(150);
            win.Header = "创建文件";
            
            StackPanel stack = new StackPanel();
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Margin = new Thickness(10);

            Label label = new Label();
            label.Content = "请输入文件名";
            stack.Children.Add(label);

            TextBox txtName = new TextBox();
            txtName.Text = "文本文件.txt";
            stack.Children.Add(txtName);

            StackPanel stackButton = new StackPanel();
            stackButton.Orientation = Orientation.Horizontal;
            stackButton.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(stackButton);

            Button btnOK = new Button();
            btnOK.Content = "创建";
            btnOK.Padding = new Thickness(10, 0, 10, 0);
            btnOK.Margin = new Thickness(10);
            stackButton.Children.Add(btnOK);
            Button btnCancel = new Button();
            btnCancel.Content = "取消";
            btnCancel.Padding = new Thickness(10, 0, 10, 0);
            btnCancel.Margin = new Thickness(10);
            stackButton.Children.Add(btnCancel);

            FadeOutWindow();

            win.Content = stack;
            win.IsModal = true;
            windowContainer.Children.Add(win);

            txtName.Focus();

            btnOK.Click += delegate
            {
                VFS.Directory dir = vfs.NewDirectory(currentDirectory);
                if (dir.Contains(txtName.Text))
                {
                    System.Windows.Forms.MessageBox.Show("该文件或文件夹已存在", "创建失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                try
                {
                    VFS.File file = vfs.NewFile(currentDirectory + txtName.Text, VFS.FileMode.CreateNew);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "创建失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
                Reload();
            };

            btnCancel.Click += delegate
            {
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
            };
        }

        private void buttonDelete_Click(object sender, RoutedEventArgs e)
        {
            var item = (DirectoryItem)listView.SelectedItem;

            if (item == null)
            {
                return;
            }

            XamDialogWindow win = CreateDialogWindow(150);
            win.Header = "删除";

            StackPanel stack = new StackPanel();
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Margin = new Thickness(10);

            Label label = new Label();
            label.Content = "您确认要删除该" + (item.isDirectory ? "文件夹" : "文件") + "吗?";
            stack.Children.Add(label);
            
            StackPanel stackButton = new StackPanel();
            stackButton.Orientation = Orientation.Horizontal;
            stackButton.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(stackButton);

            Button btnOK = new Button();
            btnOK.Content = "删除";
            btnOK.Padding = new Thickness(10, 0, 10, 0);
            btnOK.Margin = new Thickness(10);
            stackButton.Children.Add(btnOK);
            Button btnCancel = new Button();
            btnCancel.Content = "取消";
            btnCancel.Padding = new Thickness(10, 0, 10, 0);
            btnCancel.Margin = new Thickness(10);
            stackButton.Children.Add(btnCancel);

            FadeOutWindow();

            win.Content = stack;
            win.IsModal = true;
            windowContainer.Children.Add(win);
            
            btnOK.Click += delegate
            {
                VFS.Directory dir = vfs.NewDirectory(currentDirectory);
                
                try
                {
                    dir.Delete(item.name);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "删除失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    return;
                }
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
                Reload();
            };

            btnCancel.Click += delegate
            {
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
            };
        }

        private void buttonRename_Click(object sender, RoutedEventArgs e)
        {
            var item = (DirectoryItem)listView.SelectedItem;

            if (item == null)
            {
                return;
            }
            
            XamDialogWindow win = CreateDialogWindow(150);
            win.Header = "重命名";

            StackPanel stack = new StackPanel();
            stack.VerticalAlignment = VerticalAlignment.Center;
            stack.Margin = new Thickness(10);

            Label label = new Label();
            label.Content = "请输入新文件" + (item.isDirectory ? "夹" : "") + "名";
            stack.Children.Add(label);

            TextBox txtName = new TextBox();
            txtName.Text = item.name;
            stack.Children.Add(txtName);

            StackPanel stackButton = new StackPanel();
            stackButton.Orientation = Orientation.Horizontal;
            stackButton.HorizontalAlignment = HorizontalAlignment.Center;
            stack.Children.Add(stackButton);

            Button btnOK = new Button();
            btnOK.Content = "重命名";
            btnOK.Padding = new Thickness(10, 0, 10, 0);
            btnOK.Margin = new Thickness(10);
            stackButton.Children.Add(btnOK);
            Button btnCancel = new Button();
            btnCancel.Content = "取消";
            btnCancel.Padding = new Thickness(10, 0, 10, 0);
            btnCancel.Margin = new Thickness(10);
            stackButton.Children.Add(btnCancel);

            FadeOutWindow();

            win.Content = stack;
            win.IsModal = true;
            windowContainer.Children.Add(win);

            txtName.Focus();

            btnOK.Click += delegate
            {
                VFS.Directory dir = vfs.NewDirectory(currentDirectory);
                if (dir.Contains(txtName.Text))
                {
                    System.Windows.Forms.MessageBox.Show("新文件或文件夹名已存在", "重命名失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                try
                {
                    dir.Rename(item.name, txtName.Text);
                }
                catch (Exception ex)
                {
                    System.Windows.Forms.MessageBox.Show(ex.Message, "重命名失败", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error);
                    txtName.Focus();
                    return;
                }
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
                Reload();
            };

            btnCancel.Click += delegate
            {
                win.Close();
                windowContainer.Children.Remove(win);
                FadeInWindow();
            };
        }

        List<String> history = new List<String>() { "/" };
        int historyNeedle = 0;

        private void buttonBack_Click(object sender, RoutedEventArgs e)
        {
            if (historyNeedle - 1 >= 0)
            {
                historyNeedle--;
                LoadDirectory(history[historyNeedle]);
                buttonForward.IsEnabled = (historyNeedle + 1 < history.Count);
                buttonBack.IsEnabled = (historyNeedle - 1 >= 0);
            }
        }

        private void buttonForward_Click(object sender, RoutedEventArgs e)
        {
            if (historyNeedle + 1 < history.Count)
            {
                historyNeedle++;
                LoadDirectory(history[historyNeedle]);
                buttonForward.IsEnabled = (historyNeedle + 1 < history.Count);
                buttonBack.IsEnabled = (historyNeedle - 1 >= 0);
            }
        }
        
        private void textboxPath_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (history.Count - historyNeedle - 1 > 0)
                {
                    history.RemoveRange(historyNeedle + 1, history.Count - historyNeedle - 1);
                }
                var dir = textboxPath.Text;
                if (LoadDirectory(dir))
                {
                    history.Add(dir);
                    historyNeedle++;
                    buttonForward.IsEnabled = (historyNeedle + 1 < history.Count);
                    buttonBack.IsEnabled = (historyNeedle - 1 >= 0);
                }
            }
        }

        private void buttonUp_Click(object sender, RoutedEventArgs e)
        {
            GoUp();
        }
    }
}
