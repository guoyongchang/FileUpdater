﻿using FileUpdater.Helpers;
using FileUpdater.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FileUpdater {
  public partial class Updater : Form {
    private ClientConfig clientConfig;
    private ServerConfig serverConfig;
    private IList<Models.Directory> clientDirectories;
    private Icon icon;
    public Updater() {
      InitializeComponent();
      // 允许随便更改控件
      Control.CheckForIllegalCrossThreadCalls = false;
    }

    #region 窗体事件
    #region 移动窗口
    private Point mPoint = new Point();
    private void HeadBgd_MouseDown(object sender, MouseEventArgs e) {
      mPoint.X = e.X;
      mPoint.Y = e.Y;
    }
    private void HeadBgd_MouseMove(object sender, MouseEventArgs e) {
      if (e.Button == MouseButtons.Left) {
        Point myPosittion = MousePosition;
        myPosittion.Offset(-mPoint.X, -mPoint.Y);
        Location = myPosittion;
      }
    }
    #endregion
    /// <summary>
    /// 更新按钮事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void UpdateBtn_Click(object sender, EventArgs e) {
      CheckUpdate(true);
    }
    /// <summary>
    /// 窗体加载事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Updater_Load(object sender, EventArgs e) {
      LoadClientConfig();
      //展示本地版本
      this.CurrentVersionLabel.Text = clientConfig.CurrentVersion;

      if (clientConfig.Debug.HasValue && clientConfig.Debug.Value) {
        clientConfig.Debug = false;
        GenerateLocalFiles();
      }
    }
    /// <summary>
    /// 窗体显示事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Updater_Shown(object sender, EventArgs e) {
      LoadServerConfig();
      CheckUpdate();
    }
    /// <summary>
    /// 关闭事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void CloseApplication_Click(object sender, EventArgs e) {
      Application.Exit();
    }
    /// <summary>
    /// 窗体关闭事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Updater_FormClosing(object sender, FormClosingEventArgs e) {
      FileHelper.WriteToFile(clientConfig, "updater.json");
    }
    #endregion

    #region 加载Config
    /// <summary>
    /// 加载客户端Config
    /// </summary>
    /// <param name="path"></param>
    private void LoadClientConfig(string path = "./updater.json") {
      using (StreamReader fs = new StreamReader(path, Encoding.UTF8)) {
        this.clientConfig = JsonConvert.DeserializeObject<ClientConfig>(fs.ReadToEnd());
      }
    }
    /// <summary>
    /// 加载客户端Files文件
    /// </summary>
    private void LoadClientDirectories() {
      this.clientDirectories = FileHelper.GetDirectories(this.serverConfig.Directories.Select(c => c.DirName).ToArray()).GroupBy(c => c.DirName).Select(c => c.First()).ToList();
    }
    /// <summary>
    /// 加载服务端Config
    /// </summary>
    private void LoadServerConfig() {
      try {
        Uri serverConfigUri = new Uri($"{this.clientConfig.ServerUrl}{this.clientConfig.MainFile}?r={Guid.NewGuid().ToString()}");
        this.serverConfig = HttpHelper.GetJsonFile<ServerConfig>(serverConfigUri);
        LoadServerDirectories();
        LoadClientDirectories();
      } catch (Exception ex) {
        this.LatestVersionLabel.Text = "未连接到远程服务器";
        this.LatestVersionLabel.ForeColor = Color.Red;
        throw ex;

      }
    }
    /// <summary>
    /// 加载服务端Files文件
    /// </summary>
    private void LoadServerDirectories() {
      bool isAbsolutePath = Uri.TryCreate(serverConfig.FileAddress, UriKind.Absolute, out Uri uri);
      IList<Models.Directory> directories = null;
      if (isAbsolutePath) {
        directories = HttpHelper.GetJsonFile<List<Models.Directory>>(uri);
      } else {
        bool isRelativePath = Uri.TryCreate($"{clientConfig.ServerUrl}{serverConfig.FileAddress}", UriKind.Absolute, out uri);
        if (isRelativePath) {
          directories = HttpHelper.GetJsonFile<List<Models.Directory>>(uri);
        } else {
          throw new Exception($"去它妈的什么路径?{clientConfig.ServerUrl}{serverConfig.FileAddress}");
        }
      }
      this.serverConfig.Directories = directories;
    }
    private void LoadIcon(bool forceUpdate = false) {
      bool isAbsolutePath = Uri.TryCreate(serverConfig.Icon, UriKind.Absolute, out Uri uri);
      IList<Models.Directory> directories = null;
      if (!isAbsolutePath) {
        directories = HttpHelper.GetJsonFile<List<Models.Directory>>(uri);
      } else {
        bool isRelativePath = Uri.TryCreate($"{clientConfig.ServerUrl}{serverConfig.Icon}", UriKind.Absolute, out uri);
        if (isRelativePath) {
          directories = HttpHelper.GetJsonFile<List<Models.Directory>>(uri);
        } else {
          throw new Exception($"去它妈的什么路径?{clientConfig.ServerUrl}{serverConfig.Icon}");
        }
      }
      this.serverConfig.Directories = directories;
    }
    #endregion

    #region 对比差异
    /// <summary>
    /// 获取目录相对关系 方便使用
    /// </summary>
    /// <returns></returns>
    private IEnumerable<Tuple<string, IList<Models.File>, IList<Models.File>>> GetReleativeDirectories() {
      var releativeDirs =
        from serverDir in serverConfig.Directories
        join clientDir in this.clientDirectories on serverDir.DirName equals clientDir.DirName
        select new Tuple<string, IList<Models.File>, IList<Models.File>>(serverDir.DirName, serverDir.Files, clientDir.Files);
      return releativeDirs;
    }
    /// <summary>
    /// 更新
    /// </summary>
    private void Upgrading() {
      IList<Task> deleteTasks = new List<Task>();
      IList<Task> downloadTasks = new List<Task>();
      foreach (var releativeDirectory in GetReleativeDirectories()) {
        foreach (var file in releativeDirectory.Item2) {
          switch (file.Event) {
            case Enum.FileEventEnum.Add:
              bool fileExists = FileHelper.CheckFileExists(file, releativeDirectory.Item1);
              if (!fileExists) {

                deleteTasks.Add(new Task(() => {
                  this.lbl_DownloadName.Text = $"删除{file.Name}中...";
                  FileHelper.Delete(file, releativeDirectory.Item1);
                }));

                downloadTasks.Add(new Task(() => {
                  this.lbl_DownloadName.Text = $"下载{file.Name}中...";
                  HttpHelper.DownloadFile(serverConfig, file, releativeDirectory.Item1, this.UpdatePgb);
                }));
              }
              break;
            case Enum.FileEventEnum.Delete:
              deleteTasks.Add(new Task(() => {
                this.lbl_DownloadName.Text = $"删除{file.Name}中...";
                FileHelper.Delete(file, releativeDirectory.Item1);
              }));
              break;
          }
        }
      }
      foreach (var deleteTask in deleteTasks) {
        deleteTask.RunSynchronously();
      }
      foreach (var downloadTask in downloadTasks) {
        downloadTask.RunSynchronously();
      }
      UpdateSuccessful();
    }
    #endregion

    #region 提示框简单类
    public static void ShowMessageBox(bool? isError, string showText, bool showFinelExit) {
      if (!isError.HasValue) {
        MessageBox.Show(showText, "警告", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
      } else {
        if (isError.Value) {
          MessageBox.Show(showText, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } else {
          MessageBox.Show(showText, "提示", MessageBoxButtons.OK);
        }
      }
    }
    #endregion

    #region 读取本地数据生成Json
    private void GenerateLocalFiles() {
      FileHelper.WriteToFile(FileHelper.GetDirectories(clientConfig.GenerateDir.ToArray(), Enum.FileEventEnum.Add), "./serverfiles.json");
    }
    #endregion

    #region 更新检测
    private void CheckUpdate(bool forceUpdate = false) {
      lbl_DownloadName.Visible = true;
      lbl_DownloadName.ForeColor = Color.DarkOrange;
      lbl_DownloadName.Text = "检查中...";
      LatestVersionLabel.Text = serverConfig.LatestVersion;
      if (forceUpdate || !clientConfig.CurrentVersion.Equals(serverConfig.LatestVersion)) {
        LatestVersionLabel.ForeColor = Color.Red;
        if (MessageBox.Show(string.Format("发现客户端新版本:{0}\n更新内容:\n{1}\n是否更新?", serverConfig.LatestVersion, serverConfig.Remark), "发现新版本", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) == DialogResult.OK) {
          Upgrading();
          LatestVersionLabel.ForeColor = Color.PaleGreen;
        } else {
          UpdateCanceled();
        }
      } else {
        UpdateSuccessful(true);
      }
    }
    private void UpdateCanceled() {
      lbl_DownloadName.ForeColor = Color.Red;
      lbl_DownloadName.Text = "用户拒绝更新,已取消";
    }
    private void UpdateSuccessful(bool noUpdated = false) {
      if (!noUpdated) {
        clientConfig.CurrentVersion = serverConfig.LatestVersion;
        this.CurrentVersionLabel.Text = clientConfig.CurrentVersion;

        lbl_DownloadName.Text = $"完毕,可以愉悦的启动游戏了";
        lbl_DownloadName.ForeColor = Color.PaleGreen;
        LatestVersionLabel.ForeColor = Color.PaleGreen;

        ShowMessageBox(false, "更新完成!", false);
      } else {
        lbl_DownloadName.Text = $"已经是最新版本了";
        lbl_DownloadName.ForeColor = Color.PaleGreen;
        LatestVersionLabel.ForeColor = Color.PaleGreen;
      }
    }
    #endregion

    #region 图标展示
    //private void ShowIcon(string base64Icon) {
    //  using (MemoryStream ms = new MemoryStream(Convert.FromBase64String(base64Icon))) {
    //    Icon icon = new Icon(ms);
    //    this.Icon = icon;
    //    pictureBox1.Image = Bitmap.FromHicon(icon.Handle);
    //  }


    //}
    #endregion
  }
}
