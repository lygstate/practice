using System;
using System.Linq;
using System.Net;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Text;
using WinForms = System.Windows.Forms;
using IO = System.IO;
using html = HtmlAgilityPack;
using System.Collections;
using System.Collections.Generic;
using System.Threading;


namespace HttpDownloader
{
    using Properties;

    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow
    {
        List<string> urls;
        private Mutex thisLock = new Mutex();
        private Mutex downloadLock = new Mutex();
        string rootURL;
        Uri rootUri;
        public MainWindow()
        {
            InitializeComponent();

            this.LocalDirectoryBrowsButton.Click += LocalDirectoryBrowsButtonOnClick;
            this.StartDownloadButton.Click += StartDownloadButtonOnClick;
            this.LocalDirectoryTextBox.Text = Settings.Default.DownloadDirectory;
            this.rootURL = "http://nchc.dl.sourceforge.net/project/mingw/";
            this.rootUri = new Uri(this.rootURL);
            this.UrlsTextBox.Text = rootURL;

            this.DownloadProgressBar.Value = 0;
        }

        private void StartDownloadButtonOnClick(object sender, RoutedEventArgs routedEventArgs)
        {
            if (string.IsNullOrWhiteSpace(this.UrlsTextBox.Text))
            {
                MessageBox.Show("请输入要下载的URL！", "错误：", MessageBoxButton.OK, MessageBoxImage.Error);
                this.UrlsTextBox.Focus();
                return;
            }
            if (string.IsNullOrWhiteSpace(this.LocalDirectoryTextBox.Text))
            {
                MessageBox.Show("请选择保存目录！", "错误：", MessageBoxButton.OK, MessageBoxImage.Error);
                this.LocalDirectoryTextBox.Focus();
                return;
            }
            if (Settings.Default.DownloadDirectory != this.LocalDirectoryTextBox.Text)
            {
                Settings.Default.DownloadDirectory = this.LocalDirectoryTextBox.Text;
                Settings.Default.Save();
            }
            var urls = this.UrlsTextBox.Text.Split(new[] {
				Environment.NewLine
			}, StringSplitOptions.RemoveEmptyEntries);
            if (thisLock.WaitOne(0) == false)
            {
                return;
            }
            this.SaveUrlsToLocal(urls.Distinct(StringComparer.OrdinalIgnoreCase).ToList(), this.LocalDirectoryTextBox.Text);
            thisLock.ReleaseMutex();
        }

        private void SaveUrlsToLocal(List<string> inputUrls, string rootDirectory)
        {
            var cursor = this.Cursor;
            // disable the ui
            this.urls = inputUrls;
            this.Cursor = Cursors.Wait;
            this.DownloadProgressBar.Maximum = urls.Count;
            this.DownloadProgressBar.Value = 0;
            this.StartDownloadButton.Content = "下载中……";
            this.StartDownloadButton.IsEnabled = false;
            this.UrlsTextBox.IsEnabled = false;
            this.LocalDirectoryTextBox.IsEnabled = false;
            this.LocalDirectoryBrowsButton.IsEnabled = false;

            const int taskCount = 4;
            var uiSyncContext = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();
            int i = 0;
            while (i < urls.Count)
            {
                this.DownloadProgressBar.Maximum = urls.Count;
                ArrayList tasks = new ArrayList();
                int j = i;
                for (; j < urls.Count && j < i + taskCount; ++j)
                {
                    this.DownloadProgressBar.Value = j;
                    Uri uri = null;
                    try
                    {
                        uri = new Uri(urls[j]);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                    if (uri != null)
                    {
                        this.SaveUri(uri, rootDirectory);
                        //tasks.Add(Task.Factory.StartNew(() => this.SaveUri(uri, rootDirectory)).ContinueWith(t => this.DownloadProgressBar.Value += 1, uiSyncContext));
                    }
                }
                i = j;
            }
            i = 0;
        }
        private void addUri(Uri uri)
        {
            this.urls.Add(uri.ToString());
        }
        private void SaveUri(Uri uri, string rootDirectory)
        {
            StringBuilder content = new StringBuilder();
            try
            {
                // 与指定URL创建HTTP请求
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(uri);
                request.UserAgent = "Mozilla/5.0 (compatible; MSIE 9.0; Windows NT 6.1; Trident/5.0; BOIE9;ZHCN)";
                request.Method = "GET";
                request.Accept = "*/*";
                //如果方法验证网页来源就加上这一句如果不验证那就可以不写了

                /*
                request.Referer = "http://sufei.cnblogs.com";
                CookieContainer objcok = new CookieContainer();
                objcok.Add(new Uri("http://sufei.cnblogs.com"), new Cookie("键", "值"));
                objcok.Add(new Uri("http://sufei.cnblogs.com"), new Cookie("键", "值"));
                objcok.Add(new Uri("http://sufei.cnblogs.com"), new Cookie("sidi_sessionid", "360A748941D055BEE8C960168C3D4233"));
                request.CookieContainer = objcok;
                */

                //不保持连接
                request.KeepAlive = true;

                // 获取对应HTTP请求的响应
                HttpWebResponse response = (HttpWebResponse)request.GetResponse();
                var filePath = this.rootUri.MakeRelativeUri(uri).ToString();

                if (filePath == String.Empty || filePath.EndsWith("/"))
                {
                    filePath = IO.Path.Combine(filePath.TrimEnd("/".ToCharArray()), "index.html");
                }

                if (response.ContentLength == 0)
                {
                    return;
                }

                var fullPath = IO.Path.Combine(rootDirectory, Uri.UnescapeDataString(filePath));
                var dir = IO.Path.GetDirectoryName(fullPath);
                if (!IO.Directory.Exists(dir))
                {
                    IO.Directory.CreateDirectory(dir);
                }

                bool needDown = true;
                if (IO.File.Exists(fullPath))
                {
                    var info = new IO.FileInfo(fullPath);
                    if (info.Length == response.ContentLength)
                    {
                        needDown = false;
                    }
                }

                //this.StatusLabel.ContentStringFormat = "Fetching " + response.ContentType;
                if (needDown)
                {
                    // 获取响应流
                    IO.Stream sReader = response.GetResponseStream();

                    var f = new IO.FileStream(fullPath, IO.FileMode.OpenOrCreate, IO.FileAccess.Write, IO.FileShare.None);
                    // 开始读取数据
                    byte[] sReaderBuffer = new byte[256];
                    Console.WriteLine(response.ContentLength);
                    int pos = 0;
                    while (true)
                    {
                        int count = sReader.Read(sReaderBuffer, 0, 256);
                        if (count <= 0)
                        {
                            break;
                        }
                        f.Write(sReaderBuffer, 0, count);
                        pos += count;
                    }
                    // 读取结束
                    sReader.Close();
                    f.Close();
                }
                /*
                var webClient = new WebClient();
                webClient.DownloadFile(uri, fullPath);
                webClient.Dispose();
                    * */

                if (response.ContentType.IndexOf("text/html") != -1)
                {
                    var x = new html.HtmlDocument();

                    x.Load(fullPath, Encoding.GetEncoding(response.CharacterSet));

                    Queue nodes = new Queue();
                    nodes.Enqueue(x.DocumentNode);
                    while (nodes.Count != 0)
                    {
                        var node = (html.HtmlNode)nodes.Dequeue();
                        foreach (html.HtmlNode child in node.ChildNodes)
                        {
                            string attrib = child.GetAttributeValue("href", String.Empty);
                            if (attrib != String.Empty)
                            {
                                if (attrib[0] != '/' && attrib[0] != '?')
                                {
                                    //System.Console.WriteLine(attrib);
                                    this.addUri(new Uri(uri, attrib));
                                }
                            }
                            nodes.Enqueue(child);
                        }
                    }
                }
                request.Abort();
            }
            catch (Exception)
            {
                content = new StringBuilder("Runtime Error");
            }
        }

        private static string GetFilePath(Uri uri)
        {
            var host = uri.DnsSafeHost;
            var filePath = host + uri.AbsolutePath;
            filePath = filePath.Replace(IO.Path.AltDirectorySeparatorChar, IO.Path.DirectorySeparatorChar);
            if (filePath.LastIndexOf(IO.Path.DirectorySeparatorChar) == filePath.Length - 1)
            {
                filePath = filePath + "default.htm";
            }
            return filePath;
        }

        private void LocalDirectoryBrowsButtonOnClick(object sender, RoutedEventArgs routedEventArgs)
        {
            var dialog = new WinForms.FolderBrowserDialog
            {
                Description = @"选择一个目录进行保存：",
                ShowNewFolderButton = true
            };
            if (!string.IsNullOrWhiteSpace(this.LocalDirectoryTextBox.Text))
            {
                dialog.SelectedPath = this.LocalDirectoryTextBox.Text;
            }
            var dialogResult = dialog.ShowDialog();
            if (dialogResult == WinForms.DialogResult.OK)
            {
                this.LocalDirectoryTextBox.Text = dialog.SelectedPath;
            }

        }
    }
}
