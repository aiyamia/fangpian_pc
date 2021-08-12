using System.Windows;
using Newtonsoft.Json.Linq;
using System.Net.Http;

namespace fangpian_pc
{
    /// <summary>
    /// PopupWindow.xaml 的交互逻辑
    /// </summary>
    public partial class PopupWindow : Window
    {
        readonly HttpClient myclient = new HttpClient();
        private HttpResponseMessage reponse;
        private string content;
        private string meanText;
        public PopupWindow()
        {
            InitializeComponent();
        }

        private async void FetchButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                MeanTextBox.Text = "";
                PhText.Text = "";
                OriginText.Text = "";
                reponse = await myclient.GetAsync("https://dictweb.translator.qq.com/api/elementary?word=" + WordText.Text);
                content = await reponse.Content.ReadAsStringAsync();
                dynamic data = JObject.Parse(content);
                OriginText.Text = data.word.text ?? WordText.Text;
                var meanings = data["oxford_dict_info"]["abstract"];
                var ph_data = data.oxford_dict_info.ph_json ?? data.book_word_info.phonetic ?? "";
                var ph = ph_data.BrE ?? data.pronunciation ?? "";
                PhText.Text = "/" + ph + "/";
                meanText = "";
                foreach (var meaning in meanings)
                {
                    meanText += meaning.ps + " " + string.Join("；", meaning.exp) + "\n";
                }
                MeanTextBox.Text = meanText.TrimEnd('\n');
                ImageControl.Height = 0;
            }
            catch (System.Exception)
            {
                PhText.Text = "未找到";
                MeanTextBox.Text = "未找到";
                OriginText.Text = "未找到";
            }
        }
    }
}
