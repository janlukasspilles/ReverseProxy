using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ReverseProxy
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        SelfHostedWebServer selfHostedWebServer;
        HttpRedirectingProxy httpRedirectingProxy;

        private void btnStart_Click(object sender, EventArgs e)
        {
            httpRedirectingProxy = new HttpRedirectingProxy(txtListenTo.Text, txtTargetHost.Text, UpdateLog_ThreadSafe);
            selfHostedWebServer = new SelfHostedWebServer(new string[] { txtListenTo.Text }, httpRedirectingProxy.ProcessRequest, UpdateLog_ThreadSafe);
            selfHostedWebServer.LogOnEachRequest = true;
            selfHostedWebServer.Start();
        }

        public static string SendResponseMethod(HttpListenerRequest request)
        {
            return string.Format("My web page.<br>{0}", DateTime.Now);
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            selfHostedWebServer.Stop();
        }

        void UpdateLog_ThreadSafe(string newLine)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke((Action)delegate { UpdateLog(newLine); });
            }
            else
            {
                UpdateLog(newLine);
            }
            Application.DoEvents(); //repaint or respond to msg
        }

        void UpdateLog(string newLine)
        {
            txtResults.AppendText(Environment.NewLine + newLine);
        }
    }
}
