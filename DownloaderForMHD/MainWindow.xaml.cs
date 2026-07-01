using Downloader;
using DownloaderForMHR;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace DownloaderForMHD
{


    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] public static extern int MessageBoxTimeoutA(IntPtr hWnd, string msg, string Caps, int type, int Id, int time);

        ///////////////////////////////////
        ///1、变量
        #region
        private const string USER_AGENT_DEFAULT = @"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/97.0.4692.99 Safari/537.36";
        private const string PROXY_HOST_DEFAULT = "127.0.0.1";
        private const int PROXY_PORT_DEFAULT = 8580;
        private const int THREAD_NUM_DEFAULT = 3;

        private static string APP_PATH = Directory.GetCurrentDirectory();
        private string SETTINGS_JSON_FILE = APP_PATH + @"\settings.json";
        private string DOWNLOAD_HISTORY_JSON_FILE = APP_PATH + @"\download_history.json";
        ObservableCollection<DownloadItem> downloadItemList = new ObservableCollection<DownloadItem>();
        List<DownloadItem> downloadList = new List<DownloadItem>();
        ObservableTaskProgress<double> taskProgress = new ObservableTaskProgress<double>();


        DownloadHistory? downloadHistory;

        //private int oldCmbPageValueIndex = -1;
        private bool oldUseProxy = false;
        private int oldProxyPort = 0;
        private ProxyState proxyState = new ProxyState();

        int totalDownloadNum = 0;
        int countCompleted = 0;

        private string userAgent = USER_AGENT_DEFAULT;
        bool test = false; //!!!

        //20220605
        private static string DOWNLOAD_PATH = APP_PATH + @"\下载";
        private readonly int DEFAULT_THREAD_NUM = 3;
        private AtomicBoolean abStartCheck = new AtomicBoolean(false);
        private AtomicBoolean abStartDownload = new AtomicBoolean(false);
        private AtomicBoolean abStartUnzip = new AtomicBoolean(false);
        private int currentTaskId = 0;
        private object obj = new object();
        private HashSet<string> errors = new HashSet<string>();

        Settings? settings;
        bool fileWithPic = true;
        //bool needToCheckFileSize = true;

        //20231123
        CancellationTokenSource cancellationToken = new CancellationTokenSource();
        #endregion

        ///////////////////////////////////
        ///2、类
        #region
        [Serializable()]
        class Settings
        {
            public bool UseProxy { get; set; }
            public int ProxyIndexSelected { get; set; }
            public List<string>? Proxies = null;
            [JsonIgnore]
            public string ProxyHost = PROXY_HOST_DEFAULT;
            [JsonIgnore]
            public int ProxyPort = PROXY_PORT_DEFAULT;

            public int ThreadNum { get; set; }
            public bool DownloadFileType { get; set; }
            public bool LimitMaxSizeForOneFile { get; set; }
            public int MaxSizeForOneFile { get; set; }

            public List<string>? UserAgents = null;
        }

        [Serializable()]
        class ProxyState
        {
            public bool UseProxy;
            public string ProxyHost;
            public int ProxyPort;
            public bool IsProxyChanged;

            public ProxyState(bool useProxy = true, string proxyHost = PROXY_HOST_DEFAULT, int proxyPort = PROXY_PORT_DEFAULT, bool isProxyChanged = false)
            {
                UseProxy = useProxy;
                ProxyHost = proxyHost;
                ProxyPort = proxyPort;
                IsProxyChanged = isProxyChanged;
            }
        }

        [Serializable()]
        class DownloadHistory
        {
            public List<DownloadPackage> DownloadPackages { get; set; }

            public DownloadHistory(List<DownloadPackage> downloadPackages)
            {
                DownloadPackages = downloadPackages;
            }
        }


        [Serializable()]
        class DownloadItem : INotifyPropertyChanged
        {
            public int id { get; set; }
            public int DisplayId { get { return id + 1; } }
            ////// 
            public string fileName { get; set; }

            private string _downloadUrl = "";
            public string downloadUrl
            {
                get { return _downloadUrl; }
                set
                {
                    if (_downloadUrl == value)
                        return;
                    _downloadUrl = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadUrl"));
                    }
                }
            }

            public string folderPath;
            public string fullFileName;

            private int _downloadProgress;

            //80%
            public int downloadProgress
            {
                get { return _downloadProgress; }
                set
                {
                    if (_downloadProgress == value)
                        return;
                    _downloadProgress = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadProgress"));
                    }
                }
            }

            private string _downloadSpeed = "";

            // 200KB/s
            public string downloadSpeed
            {
                get { return _downloadSpeed; }
                set
                {
                    if (_downloadSpeed == value)
                        return;
                    _downloadSpeed = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("downloadSpeed"));
                    }
                }
            }

            private string _fileSize = "";
            // 1.2MB/18MB
            public string fileSize
            {
                get { return _fileSize; }
                set
                {
                    if (_fileSize == value)
                        return;
                    _fileSize = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("fileSize"));
                    }
                }
            }

            public bool downloadResult; // OK

            public DownloadService? downloadService = null;

            public event PropertyChangedEventHandler? PropertyChanged;

            public DownloadItem(int id, string fileName, string downloadUrl, string folderPath = "", bool downloadResult = false)
            {
                this.id = id;
                this.fileName = fileName.Trim();
                this.downloadUrl = downloadUrl;
                this.downloadResult = downloadResult;//20240227
                this.folderPath = string.IsNullOrWhiteSpace(folderPath) ? DOWNLOAD_PATH : folderPath;
                this.fullFileName = this.folderPath + "\\" + fileName;
                setDownloadInfo(0, "0.0KB/s", "0B", false);
            }

            public void setDownloadInfo(int downloadProgress, string downloadSpeed, string fileSize, bool downloadResult)
            {
                this.downloadProgress = downloadProgress;
                this.downloadSpeed = downloadSpeed;
                this.fileSize = fileSize;
                this.downloadResult = downloadResult;
            }

            override
            public string ToString()
            {
                return string.Format("id = {0}, fileName = {1},targetUrl = {2}, downloadProgress = {3}, downloadSpeed = {4}, fileSize = {5}, downloadResult = {6}", id, fileName, downloadUrl, downloadProgress, downloadSpeed, fileSize, downloadResult);
            }
        }

        public class ObservableTaskProgress<T> : INotifyPropertyChanged
        {
            private T? _taskProgress;
            public T? TaskProgress
            {
                get { return _taskProgress; }
                set
                {
                    _taskProgress = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("TaskProgress"));//"Value"
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        public class ObservableLastColumnWidth : INotifyPropertyChanged
        {
            private double _lastColumnWidth;
            public double LastColumnWidth
            {
                get { return _lastColumnWidth; }
                set
                {
                    _lastColumnWidth = value;
                    if (PropertyChanged != null)
                    {
                        PropertyChanged(this, new PropertyChangedEventArgs("LastColumnWidth"));//"Value"
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
        }

        #endregion

        ///////////////////////////////////
        ///3、Win初始化和关闭
        #region
        public MainWindow()
        {
            InitializeComponent();

            ShowTaskInfoOnUI("正在初始化...");
            RegisterDefaultBooleanConverter();
            this.DataContext = taskProgress;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            InitView();
            InitDownloadPath();
            ParseSettingsJsonAsync();
        }

        private void myWindows_Closed(object sender, EventArgs e)
        {
            SaveSettings();
            SaveDownloadHistory();
        }

        private void InitView()
        {
            myWindows.Title = "明慧每日文章下载器" + getApplicationVersion();
            //1.
            LvDownloadItem.ItemsSource = downloadItemList;
            //2.
            //threadNums.ForEach(num => CmbThreadNum.Items.Add(num));
            DateTime now = DateTime.Now;
            CalendarMonth.IsTodayHighlighted = false;
            CalendarMonth.DisplayDate = new DateTime(now.Year, now.Month, now.Day);
            CalendarMonth.DisplayDateEnd = new DateTime(now.Year, now.Month, now.Day);
            //
            BtnStartDownload.IsEnabled = true;
            BtnStopDownload.IsEnabled = false;


        }
        private string getApplicationVersion()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            Version? version = assembly.GetName().Version;
            if (version == null) return "";
            return $" V{version.Major:D}.{version.Minor:D}.{version.Build:D4}.{version.Revision:D4}";
        }


        private void RegisterDefaultBooleanConverter()
        {
            JsonConvert.DefaultSettings = () => new JsonSerializerSettings
            {
                Converters = new List<JsonConverter>
                {
                    new BooleanJsonConverter()
                }
            };
        }

        private async void ParseSettingsJsonAsync()
        {
            try
            {
                Task<Settings?> task = Task<Settings>.Run(() =>
                {
                    return ParseSettingsJson();
                });
                settings = await task;
                if (settings == null || settings.Proxies?.Count == 0)
                {
                    if (MessageBoxError("文件格式错误或者没有有效的设置信息！"))
                    {
                        Environment.Exit(0);
                    }
                    return;
                }
                Task<DownloadHistory?> task2 = Task<DownloadHistory>.Run(() =>
                {
                    return ParseDownloadHistoryJson();
                });
                downloadHistory = await task2;
                ShowTaskInfoOnUI("准备就绪，欢迎使用本程序！");
                //2.初始化线程数量
                CkbUseProxy.IsChecked = settings.UseProxy;
                InitProxy();
                InitOthers();
                if (downloadHistory != null && downloadHistory.DownloadPackages.Count > 0)
                {
                    InitSavedDownloadPackages(downloadHistory.DownloadPackages);
                }
                if (settings.UserAgents != null && settings.UserAgents.Count > 0)
                {
                    userAgent = settings.UserAgents[new Random().Next(settings.UserAgents.Count)];
                }
                Log($"App UserAgent = {userAgent}");
            }
            catch (Exception e)
            {
                Log(e.Message);
                if (MessageBoxError($"初始化网站及其模板出错！\n详情：{e.Message}"))
                {
                    Environment.Exit(0);
                }
            }
        }

        private Settings? ParseSettingsJson()
        {
            try
            {
                if (!File.Exists(SETTINGS_JSON_FILE))
                    return null;
                //var convertor = new JsonSerializerSettings();
                //settings.Converters.Add(new StorageConverter());
                string jsonText = File.ReadAllText(SETTINGS_JSON_FILE);
                Settings? settings = JsonConvert.DeserializeObject<Settings>(jsonText);//, settings);
                if (settings == null || settings.Proxies == null || settings.Proxies.Count == 0) return null;
                int removed = settings.Proxies.RemoveAll(item => !Regex.IsMatch(item, "(\\d+.){3}\\d+:\\d{4,5}"));
                if (settings.Proxies.Count == 0)
                {
                    settings.ProxyIndexSelected = -1;
                    SaveSettings();
                    return null;
                }
                if(removed > 0 || settings.ProxyIndexSelected <= 0 || settings.ProxyIndexSelected > settings.Proxies.Count)
                {
                    settings.ProxyIndexSelected = 1;
                    SaveSettings();
                }
                Log("ParseMainJson OK...");
                return settings;
            }
            catch (Exception e)
            {
                Log("ParseMainJson ERROR: " + e.Message);
                return null;
            }
        }

        private void SaveSettings()
        {
            var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(SETTINGS_JSON_FILE, json);
        }

        private void SaveDownloadHistory()
        {
            try
            {
                List<DownloadPackage> downloadPackages = PrepareDownloadPackageData();
                downloadHistory = new DownloadHistory(downloadPackages);
                string json = JsonConvert.SerializeObject(downloadHistory, Formatting.Indented);
                File.WriteAllText(DOWNLOAD_HISTORY_JSON_FILE, json);
            }
            catch (Exception e)
            {
                Log(e.Message);
            }
        }

        private DownloadHistory? ParseDownloadHistoryJson()
        {
            try
            {
                if (!File.Exists(DOWNLOAD_HISTORY_JSON_FILE)) return null;

                var settings = new JsonSerializerSettings();
                //settings.Converters.Add(new StorageConverter());
                string jsonText = File.ReadAllText(DOWNLOAD_HISTORY_JSON_FILE);
                DownloadHistory? downloadHistory = JsonConvert.DeserializeObject<DownloadHistory>(jsonText);//, settings);
                Log("ParseDownloadHistoryJson OK...");
                return downloadHistory;
            }
            catch (Exception e)
            {
                Log("ParseDownloadHistoryJson ERROR: " + e.Message);
                return null;
            }
        }

        private List<DownloadPackage> PrepareDownloadPackageData()//20240225 修改
        {
            var empty = new List<DownloadPackage>();
            if (downloadItemList.Count == 0) return empty;

            List<DownloadItem> savedItems;
            savedItems = downloadItemList.ToList().FindAll((DownloadItem item) =>
                                /*item.downloadService != null && item.downloadService.Package != null && */!item.downloadResult);
            return CollectDownloadPackage(savedItems);
        }

        private List<DownloadPackage> CollectDownloadPackage(List<DownloadItem> list)
        {
            var empty = new List<DownloadPackage>();
            try
            {
                if (list.Count == 0)
                {
                    Log("CollectDownloadPackage(): 没有需要保存的数据！");
                    return empty;
                }

                return list.ConvertAll(item =>
                {
                    if (item.downloadService != null && item.downloadService.Package != null)
                    {
                        item.downloadService.Package.FileName = GetRelativePath(item.fullFileName);//20251014[1] 获取保存的相对路径
                        return item.downloadService.Package;
                    }
                    else
                    {
                        var downloadPackage = new DownloadPackage();
                        downloadPackage.IsSaving = false;
                        downloadPackage.IsSaveComplete = item.downloadResult;
                        downloadPackage.SaveProgress = item.downloadProgress;
                        downloadPackage.Urls = new String[] { item.downloadUrl };
                        long.TryParse(item.fileSize, out long totalFileSize);
                        downloadPackage.TotalFileSize = totalFileSize;
                        downloadPackage.FileName = GetRelativePath(item.fullFileName);
                        downloadPackage.Chunks = null;
                        return downloadPackage;
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"CollectDownloadPackage() 出错：{ex.Message}");
                return empty;
            }
        }

        private void InitDownloadPath()
        {
            try
            {
                if (!Directory.Exists(DOWNLOAD_PATH))
                {
                    Directory.CreateDirectory(DOWNLOAD_PATH);
                }
            }
            catch (Exception ex)
            {
                Log($"InitDownloadPath： 创建 {DOWNLOAD_PATH} 失败！详情：{ex.Message}");
            }
        }

        private void InitProxy()
        {
            if (settings == null || settings.Proxies == null) return;
            //1.
            foreach (var item in settings.Proxies)
            {
                CmbProxy.Items.Add(item);
            }
            CmbProxy.SelectedIndex = settings.ProxyIndexSelected - 1;
            //2.
            ParseProxy(settings.Proxies[settings.ProxyIndexSelected - 1], out string proxyHost, out int proxyPort);
            settings.ProxyHost = proxyHost;
            settings.ProxyPort = proxyPort;
            //3.
            proxyState.UseProxy = settings.UseProxy;
            proxyState.IsProxyChanged = false;
            proxyState.ProxyHost = settings.ProxyHost;
            proxyState.ProxyPort = settings.ProxyPort;
        }

        private void InitOthers()
        {
            //1.
            if (settings == null || settings.Proxies == null) return;
            var threadIndex = CmbThreadNum.Items.IndexOf(settings.ThreadNum);
            if (threadIndex == -1)
            {
                CmbThreadNum.SelectedIndex = 2;//num = 3
                SaveSettings();
            }                
            else
                CmbThreadNum.SelectedIndex = settings.ThreadNum;
            //2.
            RbFileWithoutPic.IsChecked = !settings.DownloadFileType;
            RbFileWithPic.IsChecked = settings.DownloadFileType;
            //3.
            CkBLimitMaxFileSize.IsChecked = settings.LimitMaxSizeForOneFile;
            TbMaxFileSize.Text = settings.MaxSizeForOneFile.ToString();

        }

        private void InitSavedDownloadPackages(List<DownloadPackage> packages)
        {
            try
            {
                downloadItemList.Clear();
                if (packages == null || packages.Count == 0)
                {
                    Log("没有保存的下载信息！");
                    return;
                }
                int index = 0;
                string fileName, folderPath, directoryName;
                packages.ForEach(package =>
                {
                    fileName = Path.GetFileName(package.FileName);
                    directoryName = (Path.GetDirectoryName(package.FileName) ?? "").TrimEnd('\\');
                    folderPath = DOWNLOAD_PATH + "\\" +(string.IsNullOrEmpty(directoryName) ? "" : $@"{directoryName}");
                    DownloadItem item = new DownloadItem(index, fileName, package.Urls[0], folderPath, downloadResult: package.IsSaveComplete);
                    package.FileName = folderPath + "\\" + fileName;
                    item.downloadService = CreateDownloadService(index, package);

                    string downloadSpeed = "0.0B/s | 0.0B/s";
                    string bytesReceived = CalcMemoryMensurableUnit(package.ReceivedBytesSize);
                    string totalBytesToReceive = CalcMemoryMensurableUnit(package.TotalFileSize);
                    var fileSize = $"{bytesReceived}/{totalBytesToReceive}";//20240227 ???

                    item.setDownloadInfo((int)package.SaveProgress, downloadSpeed, fileSize, package.IsSaveComplete);
                    Log($"InitSavedDownloadPackages: {index}-{item.fileName}");
                    downloadItemList.Add(item);

                    index++;
                });

            }
            catch (Exception ex)
            {
                Log($"InitSavedDownloadPackages 出现错误：{ex.Message}");
            }
        }

        #endregion


        ///////////////////////////////////
        ///4、控件消息
        #region
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            var width = myWindows.Width - 12 - 520 - 30;
            col5.Width = width - 10;
            gvcDownloadProgress.Width = width;
        }

        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {

        }

        private async void BtnStartDownload_Click(object sender, RoutedEventArgs e)
        {
            await DownloadAll();
        }

        private void BtnStopDownload_Click(object sender, RoutedEventArgs e)
        {
            cancellationToken.Cancel();
            downloadItemList.ToList().ForEach(item =>
            {
                //if(item.downloadService != null && !item.downloadResult )
                item.downloadService?.CancelAsync();
            });
            ShowTaskInfoOnUI("下载任务被取消");
            EnableWidgesOnUI(true);
        }

        private void BtnOpenDownloadPath_Click(object sender, RoutedEventArgs e)
        {
            Process.Start("Explorer.exe", DOWNLOAD_PATH);
        }

        private void TbMaxFileSize_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!IsInputValid(e))
            {
                MessageBox.Show("仅允许输入数字和小数点！");
            }
        }

        private void TbMaxFileSize_LostFocus(object sender, RoutedEventArgs e)
        {
            if (Int32.TryParse(TbMaxFileSize.Text, out int maxFileSize))
            {
                if (maxFileSize <= 10)
                {
                    MessageBox.Show("单个文件大小应大于10MB！");
                    TbMaxFileSize.Text = "10";
                    return;
                }
                if (settings == null) return;
                settings.MaxSizeForOneFile = maxFileSize;
                SaveSettings();
            }
        }

        /**
         * 2026/5/13/2025-5-13/2025-5-13.html
         * 2026/5/13/2025-5-13-t/2025-5-13-t.html
         * 
         */
        private void CalendarMonth_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
        {
            downloadItemList.Clear();
            //needToCheckFileSize = true;
            String url, fileName, folderPath;
            for (int i = 0; i < CalendarMonth.SelectedDates.Count; i++)
            {
                GetDownloadInfo(CalendarMonth.SelectedDates[i], fileWithPic, out fileName, out url, out folderPath);
                downloadItemList.Add(new DownloadItem(i, fileName, url, folderPath, false));
            }

        }

        private void CkbUseProxy_Checked(object sender, RoutedEventArgs e)
        {
            if (settings == null) return;
            settings.UseProxy = CkbUseProxy.IsChecked ?? false;
            SaveSettings();
        }

        private void CmbProxy_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string proxy = CmbProxy.SelectedItem?.ToString() ?? "";
            if (string.IsNullOrEmpty(proxy)) return;

            ParseProxy(proxy, out string proxyHost, out int proxyPort);
            if (settings == null) return;
            settings.ProxyIndexSelected = CmbProxy.SelectedIndex + 1;
            settings.ProxyHost = proxyHost;
            settings.ProxyPort = proxyPort;
            SaveSettings();

            proxyState.IsProxyChanged = true;
            proxyState.ProxyHost = proxyHost;
            proxyState.ProxyPort = proxyPort;
        }

        private void CmbThreadNum_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (settings == null) return;
            settings.ThreadNum = Int32.Parse(CmbThreadNum.Text);
            SaveSettings();
        }

        private void RbFileWithPic_Click(object sender, RoutedEventArgs e)
        {
            if (fileWithPic)
            {
                return;
            }
            else
            {
                if (settings != null)
                    settings.DownloadFileType = true;
                fileWithPic = true;
                RbFileWithPic.IsChecked = true;
                RbFileWithoutPic.IsChecked = false;
                SaveSettings();
            }
        }

        private void RbFileWithoutPic_Click(object sender, RoutedEventArgs e)
        {
            if (!fileWithPic)
            {
                return;
            }
            else
            {
                if (settings != null)
                    settings.DownloadFileType = false;
                fileWithPic = false;
                RbFileWithoutPic.IsChecked = true;
                RbFileWithPic.IsChecked = false;
                SaveSettings();
            }
        }

        private void CkBLimitMaxFileSize_Checked(object sender, RoutedEventArgs e)
        {
            if (settings == null) return;
            settings.LimitMaxSizeForOneFile = CkBLimitMaxFileSize.IsChecked ?? false;
            SaveSettings();
        }

        #endregion

        ///////////////////////////////////
        ///5、对话框
        #region
        private void MessageBoxInformationWithoutResultOnUI(string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            });

        }

        private void MessageBoxErrorWithoutResultOnUI(string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                MessageBox.Show(this, message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }

        private bool MessageBoxInformation(string message)
        {
            return MessageBox.Show(this, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK;
        }

        private bool MessageBoxError(string message)
        {
            return MessageBox.Show(this, message, "错误", MessageBoxButton.OK, MessageBoxImage.Error) == MessageBoxResult.OK;
        }

        private bool MessageBoxErrorWith2Btns(string message)
        {
            return MessageBox.Show(this, message, "错误", MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.OK;
        }

        private bool MessageBoxQuestion(string message)
        {
            return MessageBox.Show(this, message, "询问", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
        }

        private ProgressDialog ShowProgressDialog(string message, string title = "提示")
        {
            ProgressDialog dialog = new ProgressDialog();
            dialog.Owner = this;
            dialog.Title = title;
            dialog.lbTaskInfo.Content = message;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.Show();
            return dialog;
        }

        #endregion        

        /////////////////////////////////////////////////////
        ///6. 控件控制与消息显示
        #region
        private void EnableCtrls(bool enable)
        {
            CalendarMonth.IsEnabled = enable;
            CkbUseProxy.IsEnabled = enable;
            CmbProxy.IsEnabled = enable;
            CmbThreadNum.IsEnabled = enable;
            RbFileWithoutPic.IsEnabled = enable;
            RbFileWithPic.IsEnabled = enable;
            CkBLimitMaxFileSize.IsEnabled = enable;
            TbMaxFileSize.IsEnabled = enable;
            BtnStartDownload.IsEnabled = enable;
            BtnStopDownload.IsEnabled = !enable;
        }

        private void EnableWidgesOnUI(bool isEnabled)
        {
            this.Dispatcher.Invoke(() =>
            {
                EnableCtrls(isEnabled);
            });
        }

        /// <summary>
        /// startTask=false, msg 不空时，表示有错误发生。
        /// </summary>
        /// <param name="task"></param>
        /// <param name="startTask"></param>
        /// <param name="msg"></param>
        private void CtrolWidgetsOnTask(bool startTask, string errDetail = "")
        {
            countCompleted = 0;

            string taskInfo = "";
            string errInfo = "";
            bool noErrInfo = string.IsNullOrEmpty(errDetail);

            taskInfo = startTask ? "正在下载文件" : (noErrInfo ? "完成下载任务！" : "下载出错");
            if (!noErrInfo) errInfo = $"下载出错！详情：{errDetail}";

            ShowTaskInfoOnUI(taskInfo);
            if (!noErrInfo) MessageBoxErrorWithoutResultOnUI(errInfo);
            EnableWidgesOnUI(!startTask);
            if (startTask)
                ResetProgressBar();
        }

        private void ShowTaskInfoOnUI(string msg)
        {
            this.Dispatcher.Invoke(() =>
            {
                lbTaskInfo.Content = msg;
            });
        }

        private void ResetProgressBar()
        {
            pbTaskInfo.Value = 0;
        }

        private string CalcMemoryMensurableUnit(double bytes)
        {
            double kb = bytes / 1024; // · 1024 Bytes = 1 Kilobyte 
            double mb = kb / 1024; // · 1024 Kilobytes = 1 Megabyte 
            double gb = mb / 1024; // · 1024 Megabytes = 1 Gigabyte 
            double tb = gb / 1024; // · 1024 Gigabytes = 1 Terabyte 

            string result =
                tb > 1 ? $"{tb:0.0}TB" : //0.##
                gb > 1 ? $"{gb:0.0}GB" : //0.##
                mb > 1 ? $"{mb:0.0}MB" : //0.##
                kb > 1 ? $"{kb:0.0}KB" : //0.##
                $"{bytes:0.0}B";

            result = result.Replace("/", ".");
            return result;
        }

        private void ParseProxy(string proxy, out string proxyHost, out int proxyPort)
        {
            var groups = Regex.Match(proxy, "((\\d+.){3}\\d+):(\\d{4,5})").Groups;
            if (groups.Count == 4)
            {
                proxyHost = groups[1].Value;
                proxyPort = Int32.Parse(groups[3].Value);
            }
            else
            {
                proxyHost = "";
                proxyPort = 0;
            }
        }

        private bool IsInputValid(KeyEventArgs e)
        {
            // 允许的按键：数字键、[-小数点]、删除键、退格键、方向键
            if ((e.Key >= Key.D0 && e.Key <= Key.D9) ||
            (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9) ||
            //e.Key == Key.OemPeriod ||
            e.Key == Key.Back ||
            e.Key == Key.Delete ||
            e.Key == Key.Left ||
            e.Key == Key.Right)
            {
                // 禁止组合键（如 Ctrl、Alt）
                if (Keyboard.Modifiers != ModifierKeys.None)
                {
                    e.Handled = true;
                    return false;
                }
                return true;
            }
            else
            {
                e.Handled = true; // 阻止非法输入
                return false;
            }
        }

        /**
         * https://m.minghui.org/mh/articles/2026/1/19/2026-1-19.zip    保存名：2026-01-19.zip
         * https://m.minghui.org/mh/articles/2026/1/19/2026-1-19-t.zip  保存名：2026-01-19-t.zip
         * https://m.minghui.org/mh/articles/2026/1/19/2026-1-19.txt
         */
        private void GetDownloadInfo(DateTime date, bool withPic, out String fileName, out String url, out String folderPath)
        {
            String urlPart = date.ToString("yyyy/M/d/");
            string urlFileName = date.ToString("yyyy-M-d");
            fileName = date.ToString("yyyy-MM-dd");
            if (withPic)
            {
                urlFileName += "-t";
                fileName += "-t";
            }
            urlFileName += ".zip";
            fileName += ".zip";
            folderPath = DOWNLOAD_PATH + $"\\{date.Year}\\{date.ToString("MM")}";// String.Format("\\{D}\\{0:D2}\\", date.Year, date.Month);
            //Directory.CreateDirectory(folderPath);
            url = $"https://m.minghui.org/mh/articles/{urlPart}{urlFileName}";
        }

        public void Log(string msg)
        {
            //Console.WriteLine(msg);
            Debug.WriteLine(msg);
        }

        /**
         * [20251014]2 修改 
         **/
        private string GetRelativePath(string fullFileName)
        {
            string r = fullFileName.Replace(DOWNLOAD_PATH, "");
            if (r.StartsWith('\\')) r = r.Substring(1);
            return r;
        }

        #endregion

        /////////////////////////////////////////////////////
        ///7.检查文件大小
        #region
        /* private async Task CheckDownloadFileSize()
         {
             errors.Clear();
             var list = CheckFileDownloadState();
             if (list == null) return;
             Log($"DownloadAll: proxy has Changed ={proxyState.IsProxyChanged}");

             EnableWidgesOnUI(false);
             ShowTaskInfoOnUI("正在检查文件...");
             int totalDownloadNum = list.Count;
             countCompleted = 0;
             CtrolWidgetsOnTask(true);

             downloadList.Clear();
             downloadList.AddRange(list);
             await GetFileSizeParallelForEachAsync(list);
         }*/

        private async Task CheckDownloadFileSize(List<DownloadItem> items)
        {
            Console.WriteLine($"GetFileSizeParallelForEachAsync started on thread, {Environment.CurrentManagedThreadId}");
            ParallelOptions parallelOptions = new()
            {
                MaxDegreeOfParallelism = settings?.ThreadNum ?? THREAD_NUM_DEFAULT
            };
            //var resultBag = new ConcurrentBag<long>();
            cancellationToken = new CancellationTokenSource();
            await Parallel.ForEachAsync(items, parallelOptions, async (item, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await GetFileSize(item.downloadUrl);
                Log($"文件大小：{item.downloadUrl} ==> {result}");
                if (settings != null && settings.DownloadFileType && settings.LimitMaxSizeForOneFile && result > settings.MaxSizeForOneFile * 1024 * 1024)
                {
                    item.downloadUrl = item.downloadUrl.Replace("-t.zip", ".zip");
                    Log($"文件尺寸过大信息: {item.DisplayId}, size = {result}MB, newUrl = {item.downloadUrl}");
                }
                //resultBag.Add(result);
            });
            Console.WriteLine($"GetFileSizeParallelForEachAsync completed on thread:  {Environment.CurrentManagedThreadId}");
            //return resultBag.ToList();
        }
        private async Task<long> GetFileSize(string url)
        {
            WebProxy proxy = CreateProxy();
            HttpClientHandler handler = new HttpClientHandler
            {
                Proxy = proxy,
                UseProxy = true
            };
            using (HttpClient client = new HttpClient(handler))
            {
                using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Head, url))
                {
                    using (HttpResponseMessage reponse = await client.SendAsync(request))
                    {
                        if (reponse.IsSuccessStatusCode)
                        {
                            return reponse.Content.Headers.ContentLength.GetValueOrDefault(0);
                        }
                    }
                }
            }
            return 0;
        }

        #endregion
        /////////////////////////////////////////////////////
        ///8.下载控制
        #region   
        private async Task DownloadAll()
        {
            //0.
            errors.Clear();
            //1.检查网络状态
            //2.检查当前完成状态
            var list = CheckFileDownloadState();
            //2.1 [1]获取完成，不需要从新获取
            if (list == null) return;
            totalDownloadNum = list.Count;
            downloadList.Clear();
            downloadList.AddRange(list);
            try
            {

                CtrolWidgetsOnTask(true);
                //3
                await CheckDownloadFileSize(downloadList);
                //2.3 [2] 20220622 修改代理
                Log($"DownloadAll: proxy has Changed ={proxyState.IsProxyChanged}");
                if (proxyState.IsProxyChanged)
                {
                    list.ForEach(item => item.downloadService?.ResetProxy(CreateProxy()));
                }
                //4
                ParallelOptions parallelOptions = new()
                {
                    MaxDegreeOfParallelism = settings?.ThreadNum ?? THREAD_NUM_DEFAULT
                };
                //var resultBag = new ConcurrentBag<DownloadService>();
                cancellationToken = new CancellationTokenSource();
                await Parallel.ForEachAsync(downloadList, parallelOptions, async (item, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await DownloadFile(downloadList.IndexOf(item), item);
                    //resultBag.Add(result);
                });
            }
            catch (Exception ex)
            {
                Log("Error: " + ex.Message);
                CtrolWidgetsOnTask(false, ex.Message);
                ShowTaskInfoOnUI("下载出错！");
            }

            /*List<KeyValuePair<int, DownloadItem>> downloadPairs = new List<KeyValuePair<int, DownloadItem>>();
            try
            {
                currentTaskId = threadNum - 1;
                int realNum = Math.Min(threadNum, downloadList.Count);
                for (int i = 0; i < realNum; i++)
                {
                    downloadPairs.Add(new KeyValuePair<int, DownloadItem>(i, downloadList[i]));
                }
                ParallelAction(downloadPairs);
            }
            catch (Exception e1)
            {
                Log("Error: " + e1.Message);
                CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false, e1.Message);
            }*/
        }

        /// <summary>
        /// 1、null，表示取消此次任务，不執行任務；
        /// 2、不为空，则只下载未完成的。
        /// 与 CheckDownloadUrlState() 不同，这里只有两种情况
        /// 【注意】选择和不选择，都必须把前一步的 downloadUrl 全部获取才能進入到進入 【文件下載】階段
        /// 20240226 干净世界的下载不使用此函数
        /// </summary>
        /// <returns></returns>
        private List<DownloadItem>? CheckFileDownloadState()
        {
            //1.
            if (downloadItemList.Count == 0)
            {
                MessageBoxInformation("下载列表空，没有需要下载的文件！");
                return null;
            }
            //2.
            bool isAllDownloaded = IsAllFilesDownloaded();
            if (isAllDownloaded)
            {
                if (MessageBoxQuestion("已经成功下载所有文件，如果继续则会从新下载。继续请按“确定”按钮"))
                {
                    foreach (var item in downloadItemList)
                    {
                        item.downloadResult = false;
                    }
                    return downloadItemList.ToList();
                }
                else
                {
                    return null;
                }
            }
            else
            {
                //3.【 列表被选择，且没有完成】 的Item
                return downloadItemList.Where(item => !item.downloadResult).ToList();
            }
        }

        private bool IsAllFilesDownloaded()
        {
            if (downloadItemList.Count > 0 && downloadItemList.All(item => item.downloadResult == true))
                return true;
            return false;
        }

        private void ParallelAction(List<KeyValuePair<int, DownloadItem>> downloadPairs)
        {
            Parallel.For(0, downloadPairs.Count, async index =>
            {
                await DownloadFile(downloadPairs[index]).ConfigureAwait(false);
            });
        }

        private DownloadConfiguration CreateDownloadConfiguration()
        {
            string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1";
            var cookies = new CookieContainer();
            //cookies.Add(new Cookie("download-type", "test") { Domain = "domain.com" });

            RequestConfiguration requestConfiguration = new RequestConfiguration
            {
                // config and customize request headers
                Accept = "*/*",
                CookieContainer = null,//cookies,
                Headers = new WebHeaderCollection(), // { Add your custom headers }
                KeepAlive = true,
                ProtocolVersion = HttpVersion.Version11, // Default value is HTTP 1.1
                UseDefaultCredentials = false,
                UserAgent = userAgent,//"Mozilla/5.0 (Windows NT 10.0; Win64; x64)",/*USER_AGENT_DEFAULT,//*/
                //null,//"",//20220628取消 webSiteParseList.UserAgent,//"Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:91.0) Gecko/20100101 Firefox/91.0",//$"DownloaderForMHR/{version}",
                Proxy = CreateProxy(),
            };
            int chunkCount = 1;
            return new DownloadConfiguration
            {
                // usually, hosts support max to 8000 bytes, default values is 8000
                BufferBlockSize = 10240,
                // file parts to download, default value is 1
                ChunkCount = chunkCount, //8,
                // download speed limited to 2MB/s, default values is zero or unlimited
                MaximumBytesPerSecond = 1024 * 1024 * 2,
                // the maximum number of times to fail
                MaxTryAgainOnFailover = 5,
                // release memory buffer after each 50 MB
                MaximumMemoryBufferBytes = 1024 * 1024 * 50,
                // download parts of file as parallel or not. Default value is false
                ParallelDownload = true,
                // number of parallel downloads. The default value is the same as the chunk count
                ParallelCount = 4,//?
                // timeout (millisecond) per stream block reader, default values is 1000
                Timeout = 1000,
                // set true if you want to download just a specific range of bytes of a large file
                RangeDownload = false,
                // floor offset of download range of a large file
                RangeLow = 0,
                // ceiling offset of download range of a large file
                RangeHigh = 0,
                // clear package chunks data when download completed with failure, default value is false
                //ClearPackageOnCompletionWithFailure = true,
                // minimum size of chunking to download a file in multiple parts, default value is 512
                MinimumSizeOfChunking = 1024,
                // Before starting the download, reserve the storage space of the file as file size, default value is false
                ReserveStorageSpaceBeforeStartingDownload = true,
                // config and customize request headers
                RequestConfiguration = requestConfiguration,
            };
        }

        private WebProxy CreateProxy()
        {
            Uri? uri = proxyState.UseProxy ? new Uri($"http://{proxyState.ProxyHost}:{proxyState.ProxyPort}") : null;
            return new WebProxy()
            {
                Address = uri,
                UseDefaultCredentials = false,
                Credentials = System.Net.CredentialCache.DefaultNetworkCredentials,
                BypassProxyOnLocal = true
            };
        }

        private async Task<DownloadService> DownloadFile(int taskId, DownloadItem downloadItem)
        {
            DownloadService? downloadService = downloadItem.downloadService;
            if (downloadService != null && downloadService.Package.SaveProgress >= 0)
            {
                Log($"DownloadFile 继续下载 {downloadService.TaskId} -> {taskId} : {downloadItem.fileName}...");
                downloadService.TaskId = taskId;
                await downloadService.DownloadFileTaskAsync(downloadService.Package);
                return downloadService;
            }

            Log($"DownloadFile 开启新下载 {taskId} : {downloadItem.fileName}...");
            downloadService = CreateDownloadService(taskId, null);
            downloadItem.downloadService = downloadService;

            if (string.IsNullOrWhiteSpace(downloadItem.fullFileName))
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, new DirectoryInfo(downloadItem.folderPath)).ConfigureAwait(false);
            }
            else
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, downloadItem.fullFileName).ConfigureAwait(false);
            }

            return downloadService;
        }

        private async Task<DownloadService> DownloadFile(KeyValuePair<int, DownloadItem> downloadPair)
        {
            DownloadItem downloadItem = downloadPair.Value;
            DownloadService? downloadService = downloadItem.downloadService;
            if (downloadService != null && downloadService.Package.SaveProgress >= 0)
            {
                Log($"DownloadFile 继续下载 {downloadService.TaskId} -> {downloadPair.Key} : {downloadItem.fileName}...");
                downloadService.TaskId = downloadPair.Key;
                await downloadService.DownloadFileTaskAsync(downloadService.Package);
                return downloadService;
            }

            Log($"DownloadFile 开启新下载 {downloadPair.Key} : {downloadItem.fileName}...");
            downloadService = CreateDownloadService(downloadPair.Key, null);
            downloadItem.downloadService = downloadService;

            if (string.IsNullOrWhiteSpace(downloadItem.fullFileName))
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, new DirectoryInfo(downloadItem.folderPath)).ConfigureAwait(false);
            }
            else
            {
                await downloadService.DownloadFileTaskAsync(downloadItem.downloadUrl, downloadItem.fullFileName).ConfigureAwait(false);
            }

            return downloadService;
        }

        private DownloadService CreateDownloadService(int taskId, DownloadPackage? package)
        {
            DownloadService downloadService = new DownloadService(taskId, CreateDownloadConfiguration());
            downloadService.ChunkDownloadProgressChanged += OnChunkDownloadProgressChanged;
            downloadService.DownloadProgressChanged += OnDownloadProgressChanged;
            downloadService.DownloadFileCompleted += OnDownloadFileCompleted;
            downloadService.DownloadStarted += OnDownloadStarted;
            if (package != null)
            {
                downloadService.Package = package;
            }
            return downloadService;
        }

        private void OnDownloadStarted(object? sender, DownloadStartedEventArgs e)
        {
            Log($"OnDownloadStarted: TaskId = {e.TaskId}");
            if (e.TaskId < 0)
            {
                Log($"OnDownloadStarted ERROR: TaskId = {e.TaskId}");
                return;
            }

            //DownloadItem item = downloadList.ElementAt(e.TaskId);
            Log($"OnDownloadStarted [{e.TaskId} - {e.FileName}] 开始下载...");
        }

        private async void OnDownloadFileCompleted(object? sender, AsyncDownloadCompletedEventArgs e)
        {
            Log($"OnDownloadFileCompleted: TaskId = {e.TaskId}");
            //1.
            DownloadItem item = downloadList.ElementAt(e.TaskId);

            if (e.Cancelled)
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载被取消！");
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
            }
            else if (e.Error != null)
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载出错，详情：{e.Error}。");
                errors.Add(e.Error.Message);
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
            }
            else
            {
                Log($"OnDownloadFileCompleted [{e.TaskId} 下载成功！");
                //成功后再清除，否则不能断点下载
                countCompleted++;
                if (totalDownloadNum > 0)
                    taskProgress.TaskProgress = countCompleted * 100 / totalDownloadNum;
                item.downloadResult = true;
                if (item.downloadService != null)
                    await item.downloadService.Clear();
                //解压+从新命名
                UnzipFile(item.fullFileName);
            }
            Log($"下载进度: {countCompleted}/{totalDownloadNum}");
            if (countCompleted == totalDownloadNum)
            {
                Log("下载完成");
                CtrolWidgetsOnTask(false, string.Join("\r\n", errors));
                return;
            }
            //2.完成一个，开始下一个，因此，在开始的 ParallelAction 确定好数量后就会一直延续。
            /* if (!abStartDownload.Get())
             {
                 CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false);
                 return;
             }
             List<KeyValuePair<int, DownloadItem>>? downloadPairs = TakeOneTask();
             if (downloadPairs == null)
             {
                 //没有下载任务可以执行，且所有的下载都成功
                 if (downloadItemList.ToList().All(item1 => item1.downloadResult == true))
                 {
                     CtrolWidgetsOnTask(AppTask.TASK_DOWNLOAD, false);
                 }
                 return;
             }
             Log($"OnDownloadFileCompleted >>> 开始新的下载： {downloadPairs[0].Key} - {downloadPairs[0].Value.fileName}！");
             ParallelAction(downloadPairs);*/
        }

        /*private List<KeyValuePair<int, DownloadItem>>? TakeOneTask()
        {
            lock (obj)
            {
                currentTaskId++;
                if (currentTaskId > downloadList.Count - 1)
                {
                    Log(">>> 没有更多需要下载的了...");
                    return null;
                }

                Log($"OnDownloadFileCompleted >>> 新的下载 index = {currentTaskId}！");
                List<KeyValuePair<int, DownloadItem>> downloadPairs = new List<KeyValuePair<int, DownloadItem>>();
                DownloadItem newItem = downloadList.ElementAt(currentTaskId);
                downloadPairs.Add(new KeyValuePair<int, DownloadItem>(currentTaskId, newItem));

                return downloadPairs;
            }
        }*/

        private void OnChunkDownloadProgressChanged(object? sender, Downloader.DownloadProgressChangedEventArgs e)
        {
            //DownloadItem item = downloadList[e.TaskId];
            //Log($"OnChunkDownloadProgressChanged [{e.TaskId}: ProgressId = {e.ProgressId}, ProgressPercentage = {e.ProgressPercentage}%");
        }

        private void OnDownloadProgressChanged(object? sender, Downloader.DownloadProgressChangedEventArgs e)
        {
            UpdateDwonloadInfo(e);
        }

        private void UpdateDwonloadInfo(Downloader.DownloadProgressChangedEventArgs e)
        {
            if (e.TaskId < 0)
            {
                Log($"OnDownloadProgressChanged -> UpdateTitleInfo ERROR: TaskId = {e.TaskId}");
                return;
            }
            DownloadItem item = downloadList.ElementAt(e.TaskId);

            double nonZeroSpeed = e.BytesPerSecondSpeed + 0.0001;
            int estimateTime = (int)((e.TotalBytesToReceive - e.ReceivedBytesSize) / nonZeroSpeed);
            //bool isMinutes = estimateTime >= 60;
            //string timeLeftUnit = "seconds";
            //if (isMinutes)
            //{
            //    estimateTime /= 60;
            //    //timeLeftUnit = "minutes";
            //}
            //
            //if (estimateTime < 0)
            //{
            //    estimateTime = 0;
            //    //timeLeftUnit = "unknown";
            //}

            string avgSpeed = CalcMemoryMensurableUnit(e.AverageBytesPerSecondSpeed);
            string speed = CalcMemoryMensurableUnit(e.BytesPerSecondSpeed);
            string bytesReceived = CalcMemoryMensurableUnit(e.ReceivedBytesSize);
            string totalBytesToReceive = CalcMemoryMensurableUnit(e.TotalBytesToReceive);
            string progressPercentage = $"{e.ProgressPercentage:F3}".Replace("/", ".");
            Log($"TaskID:{e.TaskId} => e.ProgressPercentage={e.ProgressPercentage}, totalBytesToReceive={totalBytesToReceive}");
            if (e.ProgressPercentage >= 100)
            {
                item.downloadResult = true;
                Log($"OnDownloadProgressChanged [{e.TaskId}] 下载完成");
            }

            item.downloadProgress = (int)e.ProgressPercentage;
            item.downloadSpeed = $"{speed}/s";//$"{speed}/s | {avgSpeed}/s";
            item.fileSize = $"{bytesReceived} / {totalBytesToReceive}";
        }



        #endregion


        /////////////////////////////////////////////////////
        ///9.解压和整理
        ///解压，无图，解压到当前目录；有图，解压到zip文件名所在目录
        #region

        /**
         * zipFilePath: C:\x\y\z\2026-01-29-t.zip
         */
        private void UnzipFile(string zipFilePath)
        {
            Log($"UnzipFile: zipFilePath = {zipFilePath}");
            try
            {
                bool withPic = zipFilePath.EndsWith("-t.zip");
                //1、解压
                string? extractPath = Path.GetDirectoryName(zipFilePath);
                string? fileNameWithoutExtension = Path.GetFileNameWithoutExtension(zipFilePath);
                if (extractPath == null || fileNameWithoutExtension == null)
                {
                    Log($"UnzipFile 出错, 文件名:{zipFilePath}");
                    return;
                }
                string dateTimeString = fileNameWithoutExtension;
                if (withPic)//有图
                {
                    dateTimeString = dateTimeString.Replace("-t", "");
                    extractPath += "\\" + Path.GetFileNameWithoutExtension(zipFilePath);
                }
                ZipFile.ExtractToDirectory(zipFilePath, extractPath);
                //2、修改文件名
                DateTime date = DateTime.Parse(dateTimeString);
                string oldName = date.ToString("yyyy-M-d");
                if (withPic)
                {
                    oldName += "-t";
                }
                oldName += ".html";
                string newName = fileNameWithoutExtension + ".html";
                File.Move(extractPath + "\\" + oldName, extractPath + "\\" + newName);
                //3、删除zip文件
                File.Delete(zipFilePath);
            }
            catch (Exception e)
            {
                Log($"UnzipFile 出错: {e.Message}");
            }

        }

        #endregion
    }
}