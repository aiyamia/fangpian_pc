using System;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Drawing;
using System.Windows.Interop;
using Tesseract;
using System.Text.RegularExpressions;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Gma.System.MouseKeyHook;
using System.Windows.Forms;
using AutoUpdaterDotNET;

namespace fangpian_pc
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private bool control = false;
        private bool alt = false;
        private PopupWindow popWin = new PopupWindow();
        private System.Windows.Media.Matrix transform;
        private System.Windows.Point mouse;
        private double x;
        private double y;
        readonly HttpClient myclient = new HttpClient();
        private HttpResponseMessage reponse;
        private string content;
        private string meanText;
        private IKeyboardMouseEvents m_GlobalHook;
        private readonly string KeyAlt = "LMenu";
        private readonly string KeyCtrl = "LControlKey";
        private WindowInteropHelper helper;
        private HwndSource hwndSource;
        
        private NotifyIcon ni;
        public MainWindow()
        {
            InitializeComponent();

            AutoUpdater.Start("https://gitee.com/aiyamia/fangpian_pc/blob/main/update/update.xml");

            ni = new NotifyIcon();
            ni.Icon = Properties.Resources.fangpian;
            ni.Visible = true;
            
            MenuItem exitMI = new MenuItem("exit", new EventHandler(Exit));
            ni.ContextMenu = new ContextMenu(new MenuItem[] { exitMI });
            ni.BalloonTipTitle = "欢迎";
            ni.BalloonTipText = "方片 助力原文阅读";
            ni.BalloonTipIcon = ToolTipIcon.Info;
            ni.ShowBalloonTip(0);

            //这两行参考：https://stackoverflow.com/a/44949487
            //不这样的话，下方PopShow函数中的PresentationSource.FromVisual(popWin)会报错“未将对象引用设置到对象的实例。”
            helper = new WindowInteropHelper(popWin);
            hwndSource = HwndSource.FromHwnd(helper.EnsureHandle());

            //在窗口popWin外点击会隐藏窗口
            popWin.Deactivated += (sender, args) => { 
                popWin.Hide();
                popWin.WordText.Text = "";
                popWin.MeanTextBox.Text = "";
                popWin.PhText.Text = "";
                popWin.OriginText.Text = "";
                popWin.ImageControl.Height = 0;
            };
            Subscribe();
        }
        public void Subscribe()
        {
            // Note: for the application hook, use the Hook.AppEvents() instead
            m_GlobalHook = Hook.GlobalEvents();

            m_GlobalHook.MouseDownExt += GlobalHookMouseDownExt;
            m_GlobalHook.KeyDown += GlobalHookKeyDown;
            m_GlobalHook.KeyUp += GlobalHookKeyUp;
        }
        private void GlobalHookKeyDown(object sender, KeyEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (e.KeyCode.ToString() == KeyCtrl)
                {
                    control = true;
                    controlText.Text = "true";
                }else if (e.KeyCode.ToString() == KeyAlt)
                {
                    alt = true;
                    altText.Text = "true";
                }
            });
        }
        private void GlobalHookKeyUp(object sender, KeyEventArgs e)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (e.KeyCode.ToString() == KeyCtrl)
                {
                    control = false;
                    controlText.Text = "false";
                }else if (e.KeyCode.ToString() == KeyAlt)
                {
                    alt = false;
                    altText.Text = "false";
                }
            });
        }

        private void GlobalHookMouseDownExt(object sender, MouseEventExtArgs e)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                //这里之所以用DOWN而不是UP是因为要防止与“鼠标左键选词紧接着（在左键未抬起之前）Ctrl + C复制”这个操作冲突
                if (e.Button == MouseButtons.Left && alt)
                {
                    //control = false;
                    //controlText.Text = "false";
                    alt = false;
                    altText.Text = "false";

                    if (PresentationSource.FromVisual(popWin) == null)
                    {
                        popWin = new PopupWindow();
                        popWin.Deactivated += (s, args) => {
                            popWin.Hide();
                            popWin.MeanTextBox.Text = "";
                            popWin.PhText.Text = "";
                            popWin.WordText.Text = "";
                            popWin.OriginText.Text = "";
                        };
                    }
                    await CaptureSreen(e.X, e.Y, 300, 100);
                }
            });
        }
        public void Unsubscribe()
        {
            m_GlobalHook.MouseDownExt -= GlobalHookMouseDownExt;
            m_GlobalHook.KeyDown -= GlobalHookKeyDown;

            //It is recommened to dispose it
            m_GlobalHook.Dispose();
        }
        

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr onj);

        private async Task CaptureSreen(int MouseX, int MouseY, int RegionWidth, int RegionHeight)
        {
            PopShow(MouseX, MouseY);
            Bitmap bitmap;
            bitmap = new Bitmap(RegionWidth, RegionHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(MouseX - RegionWidth / 2, MouseY - RegionHeight / 2, 0, 0, bitmap.Size);
            }
            IntPtr handle = IntPtr.Zero;

            try
            {
                handle = bitmap.GetHbitmap();
                try
                {
                    using (var engine = new TesseractEngine(@"./Resources/tessdata", "eng", EngineMode.Default))
                    {
                        using (var page = engine.Process(bitmap))
                        {
                            var str = page.GetTsvText(0);

                            var rects = page.GetSegmentedRegions((PageIteratorLevel)3);
                            bool hasText = false;
                            foreach (Rectangle item in rects)
                            {
                                if (item.Contains(RegionWidth / 2, RegionHeight / 2)|| item.Contains(RegionWidth / 2, RegionHeight / 2-item.Height))
                                {
                                    hasText = true;
                                    string regexp = @"\t" + item.X + @"\t" + item.Y + @"\t" + item.Width + @"\t" + item.Height + @"\t-?\d+\t(.*)\n";
                                    GroupCollection gs = Regex.Match(str, regexp).Groups;
                                    string word = Regex.Replace(gs[1].Value,@"\W","");
                                    popWin.WordText.Text = word;
                                    try
                                    {
                                        popWin.MeanTextBox.Text = "";
                                        popWin.PhText.Text = "";
                                        popWin.OriginText.Text = "";
                                        reponse = await myclient.GetAsync("https://dictweb.translator.qq.com/api/elementary?word=" + popWin.WordText.Text);
                                        content = await reponse.Content.ReadAsStringAsync();
                                        dynamic data = JObject.Parse(content);
                                        popWin.OriginText.Text = data.word.text ?? word;
                                        var meanings = data["oxford_dict_info"]["abstract"];
                                        var ph_data = data.oxford_dict_info.ph_json ?? data.book_word_info.phonetic ?? "";
                                        var ph = ph_data.BrE ?? data.pronunciation ?? "";
                                        popWin.PhText.Text = "/" + ph + "/";
                                        meanText = "";
                                        foreach (var meaning in meanings)
                                        {
                                            meanText += meaning.ps + " " + string.Join("；", meaning.exp) + "\n";
                                        }
                                        popWin.MeanTextBox.Text = meanText.TrimEnd('\n');
                                        popWin.ImageControl.Height = 0;
                                    }
                                    catch (System.Exception)
                                    {
                                        popWin.PhText.Text = "未找到";
                                        popWin.MeanTextBox.Text = "未找到";
                                        popWin.OriginText.Text = "未找到";
                                        popWin.ImageControl.Height = 130;
                                        popWin.ImageControl.Source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                                    }
                                    //PopShow(MouseX, MouseY);
                                    break;
                                }
                            }
                            if (!hasText)
                            {
                                popWin.WordText.Text = "未选中单词";
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Trace.TraceError(e.ToString());
                }
                
            }
            catch (Exception)
            {
            }

            finally
            {
                DeleteObject(handle);
            }
        }

        private void PopShow(int Ex, int Ey)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                popWin.Show();
                //这两行很重要，不使用的话，会让鼠标和窗口之间有个距离，来自https://stackoverflow.com/a/19790851
                transform = hwndSource.CompositionTarget.TransformFromDevice;
                System.Drawing.Point point = System.Windows.Forms.Control.MousePosition;
                mouse = transform.Transform(new System.Windows.Point(Ex, Ey));

                x = mouse.X;
                y = mouse.Y;
                popWin.Left = x + 20;
                popWin.Top = y + 20;
                //让窗口显示在最上层
                
                popWin.Activate();
                //popWin.Focus();
                popWin.Topmost = true;
                popWin.Topmost = false;
            });
        }
        private void Exit(object sender, EventArgs e)
        {
            ni.Visible = false;
            //ni.Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Unsubscribe();//这步没有的话会在退出时光标卡顿
            Environment.Exit(Environment.ExitCode);
        }
    }
}
