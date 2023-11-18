using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ATA_GUI.Classes;
using ATA_GUI.Forms;
using ATA_GUI.Utils;
using Ionic.Zip;
using Newtonsoft.Json.Linq;

namespace ATA_GUI
{
    public partial class MainForm : Form
    {
        [DllImportAttribute("user32.dll")]
        public static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImportAttribute("user32.dll")]
        public static extern bool ReleaseCapture();

        private readonly ATA ata = new();

        private static readonly int WM_NCLBUTTONDOWN = 0xA1;
        private static readonly int HT_CAPTION = 0x2;
        private static readonly Regex regex = new(@"\s+");
        private WaitingForm waitingForm;

        public static string RemoveWhiteSpaces(string str)
        {
            return regex.Replace(str, string.Empty);
        }

        public MainForm()
        {
            InitializeComponent();
        }

        private void panelTopBar_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _ = ReleaseCapture();
                _ = SendMessage(Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);

                if (ata.IsMaximize)
                {
                    ata.IsMaximize = false;
                    pictureBoxMaximize.Image = Properties.Resources.icons8_maximize_button_16;
                    Size = ata.windowSize;
                }
            }
        }

        private async void buttonSyncApp_Click(object sender, EventArgs e)
        {
            if (await checkAdbFastboot(false))
            {
                switch (ata.CurrentTab)
                {
                    case Tab.SYSTEM:
                        SyncDevice();
                        break;
                    case Tab.FASTBOOT:
                        string[] log = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("getvar all"), 1).Split(' ', '\n');
                        for (int a = 0; a < log.Count(); a++)
                        {
                            if (log[a].Contains("partition-type:userdata:"))
                            {
                                labelUDT.Text = log[a]["partition-type:userdata:".Length..];
                            }
                            else if (log[a].Contains("partition-type:cache:"))
                            {
                                labelCDT.Text = log[a]["partition-type:cache:".Length..];
                            }
                            else if (log[a].Contains("unlocked:"))
                            {
                                labelBootloaderStatus.Text = log[a].Contains("yes") ? "Yes" : "No";
                            }
                        }
                        panelFastboot.Enabled = true;
                        LogWriteLine("[ OK ] info extracted");
                        break;
                    case Tab.RECOVERY:
                        if (ATA.CurrentDeviceSelected.sameMode(Tab.RECOVERY))
                        {
                            panelRecovery.Enabled = true;
                            groupBoxFlash.Enabled = ATA.CurrentDeviceSelected.Mode == DeviceMode.SIDELOAD;
                            groupBoxRecoveryRM.Enabled = ATA.CurrentDeviceSelected.Mode != DeviceMode.SIDELOAD;
                        }
                        else
                        {
                            panelRecovery.Enabled = false;
                        }
                        break;
                    default:
                        MessageShowBox("Can not sync your device in this page", 1);
                        break;
                }
            }
        }

        public static string commandAssembler(bool isAdb, string command)
        {
            return string.Format("{0} -s {1} {2}", isAdb ? "adb" : "fastboot", ATA.CurrentDeviceSelected.ID, command);
        }

        public static string commandAssemblerF(string command)
        {
            return string.Format("-s {0} {1}", ATA.CurrentDeviceSelected.ID, command);
        }

        private void SyncDevice()
        {
            try
            {
                _ = Invoke((MethodInvoker)delegate
                {
                    textBoxSearch.Text = "Search";
                    textBoxPort.Text = "5555";
                });
                ExtractDeviceData();
            }
            catch (Exception ex)
            {
                MessageShowBox(ex.Message, 0);
            }
        }

        private void buttonLogClear_Click(object sender, EventArgs e)
        {
            richTextBoxLog.Clear();
        }

        private void LogWriteLine(string str)
        {
            Invoke(delegate
            {
                richTextBoxLog.Text += str + "\n";
                richTextBoxLog.SelectionStart = richTextBoxLog.Text.Length;
                richTextBoxLog.ScrollToCaret();
            });
        }

        /* Type 0 Error, Type 1 Warning, Type 2 Info */
        public static void MessageShowBox(string message, int type)
        {
            switch (type)
            {
                case 0:
                    _ = MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    break;
                case 1:
                    _ = MessageBox.Show(message, "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    break;
                case 2:
                    _ = MessageBox.Show(message, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    break;
                default:
                    MessageShowBox("MessageShowBox generic error", 0);
                    break;
            }
        }

        private async void updateCheckAsync()
        {
            if (ata.IsConnected)
            {
                try
                {
                    _ = await ATA.CheckVersion((currentRelease, latestRelease, jsonReal) =>
                    {
                        Invoke(delegate
                        {
                            if (Utils.Version.CompareVersions(latestRelease, currentRelease) == Utils.Version.VersionComparisonResult.GreaterThan)
                            {
                                if (MessageBox.Show("New version found: " + latestRelease + "\nCurrent Version: " + ATA.CURRENTVERSION + "\n\nDo you want to update it?", "Update found!", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                {
                                    ConsoleProcess.openLink((string)jsonReal[0]["html_url"]);
                                    jsonReal[0]["assets"][0].TryGetValue("browser_download_url", out JToken urlDownload);
                                    UpdateForm update = new(urlDownload.ToString());
                                    _ = update.ShowDialog();
                                }
                                else
                                {
                                    LogWriteLine("[WARNING] your ATA is not up to date. It is recommended that you update it as soon as possible to ensure optimal performance and security");
                                }
                            }
                            else
                            {
                                LogWriteLine("[ OK ] ATA is up to date!");
                            }
                        });
                        return true;
                    }
                    );
                }
                catch
                {
                    LogWriteLine("[ERROR] A timeout error occurred while attempting to connect to the server. Please check your network connection and try again");
                    LogWriteLine("Please open the settings menu to check if a new version of the software is available for download");
                }
            }
            else
            {
                MessageShowBox("You are offline, ATA can not be updated!", 0);
            }
        }

        private void ExtractDeviceData()
        {
            string errorMessage = null;

            if (ATA.CurrentDeviceSelected == null)
            {
                return;
            }

            LogWriteLine("[INFO] checking device...");

            try
            {
                string sdk = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell getprop ro.build.version.sdk"), 0);
                string version = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell getprop ro.build.version.release"), 0);

                if (sdk.Length > 0)
                {
                    if (sdk.Any(char.IsDigit))
                    {
                        if (int.Parse(sdk) > 20)
                        {
                            string[] arrayDeviceInfoCommands = { "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.build.version.release", "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.build.user",
                                        "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.product.cpu.abilist", "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.product.manufacturer" , "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.product.model",
                                        "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.product.board", "-s "+ ATA.CurrentDeviceSelected.ID +" shell getprop ro.product.device"};
                            string deviceinfo = ConsoleProcess.adbFastbootCommandR(arrayDeviceInfoCommands, 0);
                            string[] arrayDeviceInfo = deviceinfo.Split('\n');

                            if (arrayDeviceInfo.Length > 6)
                            {
                                string localIp = "";

                                try
                                {
                                    localIp = ConsoleProcess.systemCommand("adb.exe -s " + ATA.CurrentDeviceSelected.ID + " shell ip route").Split(' ').Where(it => Regex.Match(it, "(\\b25[0-5]|\\b2[0-4][0-9]|\\b[01]?[0-9][0-9]?)(\\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}").Success).Last();
                                }
                                catch
                                {
                                    localIp = "";
                                }

                                Invoke(delegate
                                {
                                    toolStripLabelTotalApps.Text = "Total: 0";
                                    checkedListBoxApp.Items.Clear();

                                    if (int.TryParse(Regex.Replace(arrayDeviceInfo[0], "([.][^.]*)", ""), out int version))
                                    {
                                        ATA.CurrentDeviceSelected.Version = version;
                                    }

                                    for (int i = 0; i < arrayDeviceInfo.Length; i++)
                                    {
                                        arrayDeviceInfo[i] = arrayDeviceInfo[i].Trim().Length > 0 ? arrayDeviceInfo[i] : "unknown";
                                    }

                                    labelAV.Text = arrayDeviceInfo[0];
                                    labelBU.Text = arrayDeviceInfo[1];
                                    labelCA.Text = arrayDeviceInfo[2];
                                    labelManu.Text = arrayDeviceInfo[3];
                                    labelModel.Text = arrayDeviceInfo[4];
                                    labelB.Text = arrayDeviceInfo[5];
                                    labelD.Text = arrayDeviceInfo[6];

                                    if (arrayDeviceInfo.Length > 6)
                                    {
                                        if (localIp.Length > 4)
                                        {
                                            comboBoxIP.Text = labelIP.Text = localIp.Trim();
                                        }
                                        if (labelIP.Text.Contains("t of devices attached") || localIp.Length == 0)
                                        {
                                            labelIP.Text = "Not connected to a network";
                                            comboBoxIP.ResetText();
                                            buttonConnectToIP.Enabled = false;
                                            buttonDisconnectIP.Enabled = false;
                                        }
                                    }
                                    if (ATA.CurrentDeviceSelected.Connection == DeviceConnection.WIRELESS)
                                    {
                                        labelStatus.Text = "Wireless";
                                        buttonConnectToIP.Enabled = false;
                                        buttonDisconnectIP.Enabled = true;
                                    }
                                    else
                                    {
                                        labelStatus.Text = "Cable";
                                        buttonConnectToIP.Enabled = true;
                                        buttonDisconnectIP.Enabled = false;
                                    }

                                    if (ATA.CurrentDeviceSelected.Version > 0)
                                    {
                                        string[] usersList = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell pm list users"), 0).Split("\n");
                                        string userTmp = usersList.Where(it => it.Contains("running")).FirstOrDefault();
                                        userTmp ??= usersList[1];
                                        int gPIndex = userTmp.IndexOf('{') + 1;
                                        labelUser.Text = ATA.CurrentDeviceSelected.User = userTmp[gPIndex..userTmp.IndexOf(':')].Trim();
                                    }

                                    groupBoxFreeRotation.Enabled = int.TryParse(ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell cmd display get - displays"), 0), out int maxDisplay);

                                    for (int i = maxDisplay; i > -1; i--)
                                    {
                                        _ = domainUpDownFreeRotation.Items.Add(i);
                                    }

                                    loadApps(AppMode.NONSYSTEM);

                                    ATA.CurrentDeviceSelected.IsRotationFreeEnabled = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + " shell wm get-ignore-orientation-request", 0).Contains("true");
                                    buttonSetRotation.Text = ATA.CurrentDeviceSelected.IsRotationFreeEnabled ? "Unset" : "Set";

                                    LogWriteLine("[ OK ] device info extracted");
                                    disableEnableSystem(true);
                                });
                            }
                            else
                            {
                                errorMessage = "android version not supported";
                            }
                        }
                        else
                        {
                            errorMessage = "android versions under 5.0 are not supported";
                        }
                    }
                    else
                    {
                        errorMessage = "device not supported";
                    }
                }
                else
                {
                    errorMessage = "android version not found";
                }
            }
            catch (Exception e)
            {
                errorMessage = e.Message;
            }

            if (errorMessage != null)
            {
                reloadList();

                Invoke(delegate
                {
                    disableEnableSystem(false);
                    buttonDisconnectIP.Enabled = false;
                    LogWriteLine("[ERROR] failed to extract device info!");
                    MessageShowBox("Failed to extract device info\nError message: " + errorMessage, 0);
                });
            }
        }

        private void loadApps(AppMode appMode)
        {
            Invoke(delegate
            {
                labelSelectedAppCount.Text = "Selected App: 0";
                checkedListBoxApp.Items.Clear();
            });

            string command = "";
            string appStringList = null;
            List<string> customApps = new();

            ATA.CurrentDeviceSelected.AppsString.Clear();
            ATA.CurrentDeviceSelected.AppMode = appMode;

            switch (appMode)
            {
                case AppMode.ALL:
                    command = commandAssemblerF("shell pm list packages --user " + ATA.CurrentDeviceSelected.User);
                    break;
                case AppMode.SYSTEM:
                    command = commandAssemblerF("shell pm list packages -s --user " + ATA.CurrentDeviceSelected.User);
                    break;
                case AppMode.NONSYSTEM:
                    command = commandAssemblerF("shell pm list packages -3 --user " + ATA.CurrentDeviceSelected.User);
                    break;
                case AppMode.DISABLE:
                    command = commandAssemblerF("shell pm list packages -d --user " + ATA.CurrentDeviceSelected.User);
                    break;
                case AppMode.UNINSTALLED:
                    string allAppString = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + " shell pm list packages --user " + ATA.CurrentDeviceSelected.User, 0);

                    if (allAppString != null)
                    {
                        command = commandAssemblerF("shell pm list packages -u --user " + ATA.CurrentDeviceSelected.User);

                        string customAppString = ConsoleProcess.adbFastbootCommandR(command, 0);

                        if (customAppString != null)
                        {
                            foreach (string item in customAppString.Split('\n'))
                            {
                                if (!allAppString.Contains(item))
                                {
                                    customApps.Add(item.Trim());
                                }
                            }
                        }
                    }
                    break;
            }

            LogWriteLine("[INFO] loading apps...");

            if (ATA.CurrentDeviceSelected.AppMode != AppMode.UNINSTALLED)
            {
                appStringList = ConsoleProcess.adbFastbootCommandR(command, 0);
            }

            if (appStringList != null || customApps.Count > 0)
            {
                try
                {
                    sortApks(appStringList != null ? appStringList.Split('\n') : customApps.ToArray());

                    Invoke(delegate
                    {
                        checkedListBoxApp.Items.AddRange(ATA.CurrentDeviceSelected.AppsString.ToArray());
                        checkedListBoxApp.CheckOnClick = true;
                        toolStripLabelTotalApps.Text = "Total: " + checkedListBoxApp.Items.Count;
                        checkBoxSelectAll.Checked = false;
                    });

                    LogWriteLine("[ OK ] apps loaded");
                }
                catch
                {
                    MessageShowBox("Error during apk loading", 0);
                }
            }
        }

        private void sortApks(string[] arrayApkTmp)
        {
            foreach (string line in arrayApkTmp)
            {
                if (line.Contains("package:"))
                {
                    ATA.CurrentDeviceSelected.AppsString.Add(line[8..].Trim() + "\n");
                }
            }
            ATA.CurrentDeviceSelected.AppsString.Sort();
        }

        private void disableEnableSystem(bool enable)
        {
            textBoxPort.Enabled = !enable;
            buttonMobileScreenShare.Enabled = enable;
            groupBoxDeviceInfo.Enabled = enable;
            groupBoxRebootMenu.Enabled = enable;
            groupBoxAPKMenu.Enabled = enable;
            buttonDeviceLogs.Enabled = enable;
            buttonTaskManager.Enabled = enable;
            tabPageTools.Enabled = enable;
            groupBoxTerminal.Enabled = enable;
        }

        private void textboxClick(object sender, EventArgs e)
        {
            if (textBoxSearch.Text == "Search" && !ata.TextboxClear)
            {
                textBoxSearch.Clear();
                ata.TextboxClear = true;
            }
        }

        private void buttonRS_Click(object sender, EventArgs e)
        {
            if (ATA.CurrentDeviceSelected.Connection == DeviceConnection.CABLE || MessageBox.Show("Adb is not able to check if the device rebooted via wireless mode, do you want to continue?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                rebootSmartphone();
            }
        }

        private void buttonRR_Click(object sender, EventArgs e)
        {
            if (ATA.CurrentDeviceSelected.Connection == DeviceConnection.CABLE || MessageBox.Show("Adb is not able to check if the device rebooted via wireless mode, do you want to continue?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _ = ConsoleProcess.systemCommand(commandAssembler(true, "reboot recovery"));
                LogWriteLine("[INFO] rebooted");
                Thread.Sleep(1000);
                reloadList();
            }
        }

        private void buttonRF_Click(object sender, EventArgs e)
        {
            if (ATA.CurrentDeviceSelected.Connection == DeviceConnection.CABLE || MessageBox.Show("Adb is not able to check if the device rebooted via wireless mode, do you want to continue?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _ = ConsoleProcess.systemCommand(commandAssembler(true, "reboot-bootloader"));
                LogWriteLine("[INFO] rebooted");
                Thread.Sleep(1000);
                reloadList();
            }
        }

        public void ToolTipGenerator(Control button, string title, string message)
        {
            ToolTip buttonToolTip = new()
            {
                ToolTipTitle = title,
                UseFading = true,
                UseAnimation = true,
                IsBalloon = true,
                ShowAlways = true,
                AutoPopDelay = 0,
                InitialDelay = 0,
                ReshowDelay = 0
            };
            buttonToolTip.SetToolTip(button, message);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            radioButtonBoot.Checked = true;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            ToolTipGenerator(buttonConnectToIP, "Connect device", "Connect to your device with this IP");
            ToolTipGenerator(buttonDisconnectIP, "Disconnect device", "Disconnect from your device with this IP");
            ToolTipGenerator(buttonMobileScreenShare, "Share Device Screen", "Your device screen will be shared on your PC");
            ToolTipGenerator(buttonLogClear, "Clear log", "Log will be clean from the box");
            ToolTipGenerator(buttonRR, "Reboot to recovery", "Your device will be rebooted to recovery");
            ToolTipGenerator(buttonRS, "Reboot to system", "Your device will be rebooted to system");
            ToolTipGenerator(buttonRF, "Reboot to fastboot", "Your device will be rebooted to fastboot");
            ToolTipGenerator(buttonReloadDevicesList, "Reload devices", "The devices list will be updated");
            ToolTipGenerator(buttonSyncApp, "Sync smartphone", "All the info needed will be extracted from your smartphone");
            ToolTipGenerator(buttonRebootRecovery, "Reboot to recovery", "Your device will be rebooted to recovery");
            ToolTipGenerator(groupBox2, "File Transfer", "Drop a file in this box and it will be trasfered in a new folder called ATA inside your device");
            ToolTipGenerator(groupBox6, "Apk Installer", "Drop an apk file and it will be installed inside your device");

            if (File.Exists(ATA.IPFileName))
            {
                foreach (string ip in File.ReadAllLines(ATA.IPFileName))
                {
                    _ = ata.IPList.Add(ip.Trim());
                }
                comboBoxIP.Items.AddRange(ata.IPList.ToArray());
            }

            comboBoxDevices.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxCameraModes.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBoxCameraModes.SelectedIndex = 0;
            groupBox6.AllowDrop = true;
            groupBox2.AllowDrop = true;
            Disclaimer disclaimer = new();
            ata.windowSize = Size;
            if (!disclaimer.checkDiscalimer())
            {
                _ = disclaimer.ShowDialog();
            }
            panelFastboot.Enabled = false;
            WindowState = FormWindowState.Minimized;
            WindowState = FormWindowState.Normal;
            _ = Focus();
        }

        private async Task<bool> DevicesListUpdate()
        {
            return await deviceListExtractor(ata.CurrentTab != Tab.FASTBOOT);
        }

        private async Task<bool> deviceListExtractor(bool isAdb)
        {
            List<string> devicesRaw;

            if (await checkAdbFastboot(isAdb))
            {
                LogWriteLine("[INFO] searching for devices...");

                string dev = ConsoleProcess.adbFastbootCommandR("devices", isAdb ? 0 : 1);

                if (dev != null)
                {
                    ata.Devices.Clear();
                    List<DeviceData> devices = new();
                    devicesRaw = dev.Split('\n').Where(it => it.Contains('\t')).ToList();

                    devicesRaw.ForEach(it =>
                    {
                        string id = it.Split('\t')[0];
                        string name = "";
                        DeviceConnection deviceConnection = DeviceConnection.CABLE;
                        DeviceData deviceData;

                        if (isAdb)
                        {
                            name = ConsoleProcess.adbFastbootCommandR("-s " + Regex.Replace(id, @"\s", "") + " shell getprop ro.product.model", 0);

                            if (Regex.Match(id, "(\\b25[0-5]|\\b2[0-4][0-9]|\\b[01]?[0-9][0-9]?)(\\.(25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)){3}").Success)
                            {
                                deviceConnection = DeviceConnection.WIRELESS;
                            }
                        }

                        deviceData = new DeviceData(name, id, DeviceMode.UNKNOWN, deviceConnection);

                        if (it.Contains("recovery"))
                        {
                            deviceData.Mode = DeviceMode.RECOVERY;
                        }
                        else if (it.Contains("device"))
                        {
                            deviceData.Mode = DeviceMode.SYSTEM;
                        }
                        else if (it.Contains("unauthorized"))
                        {
                            deviceData.Mode = DeviceMode.UNAUTHORIZED;
                        }
                        else if (it.Contains("fastboot"))
                        {
                            deviceData.Mode = DeviceMode.FASTBOOT;
                        }
                        else if (it.Contains("sideload"))
                        {
                            deviceData.Mode = DeviceMode.SIDELOAD;
                        }

                        devices.Add(deviceData);
                    });

                    _ = Invoke((MethodInvoker)delegate
                    {
                        foreach (DeviceData device in devices.Where(iterator => iterator.sameMode(ata.CurrentTab)))
                        {
                            _ = comboBoxDevices.Items.Add(string.Format("{0} [{1}] [{2}]", device.Mode is DeviceMode.RECOVERY or DeviceMode.SYSTEM ? device.Name : device.ID, device.Mode.ToString(), device.getConnectionSymbol()));
                            ata.Devices.Add(device);
                        }

                        if (comboBoxDevices.Items.Count > 0)
                        {
                            comboBoxDevices.SelectedIndex = 0;
                            ATA.CurrentDeviceSelected = ata.Devices[0];
                        }

                        buttonSyncApp.Enabled = comboBoxDevices.Items.Count > 0;

                        switch (ata.Devices.Count)
                        {
                            case 0:
                                disableEnableSystem(false);
                                panelFastboot.Enabled = false;
                                panelRecovery.Enabled = false;
                                LogWriteLine("[ERROR] no devices detected. If this problem continues, ensure that USB debugging is enabled on your device. Additionally, verify that the correct drivers are installed; you can find the necessary links under Help -> OEM Drivers. For detailed instructions on enabling USB debugging and more information, please refer to this video tutorial: https://www.youtube.com/watch?v=W7nkxS9LMXs.");
                                break;
                            case 1:
                                LogWriteLine("[ OK ] one device has been found");
                                break;
                            default:
                                LogWriteLine("[ OK ] more than one device has been found");
                                break;
                        }
                    });
                }
                else
                {
                    MessageShowBox("Something went wrong with adb", 1);
                }
            }

            return false;
        }

        public static bool pingCheck()
        {
            Ping myPing = new();
            PingReply reply;
            try
            {
                reply = myPing.Send(IPAddress.Parse("1.1.1.1"), 1000, new byte[32], new PingOptions());
                if (reply.Status == IPStatus.Success)
                {
                    return true;
                }
            }
            catch
            {
                MessageShowBox("Error during connection check", 0);
            }
            return false;
        }

        private void MainForm_Shown(object sender, EventArgs e)
        {
            if (Feedback.checkFeedbackFile())
            {
                _ = new Feedback().ShowDialog();
            }

            LogWriteLine("[INFO] checking connection...");

            toolStripButtonRestoreApp.Enabled = false;
            Enabled = false;

            waitingForm = new()
            {
                Owner = this
            };
            Thread thread = new(DataLoadingUI);
            thread.Start();
            _ = waitingForm.ShowDialog();
        }

        private async void DataLoadingUI()
        {
            ata.IsConnected = pingCheck();

            if (ata.IsConnected)
            {
                updateCheckAsync();
            }
            else
            {
                LogWriteLine("[WARNING] you are offline");
                disableSystem(true);
            }

            LogWriteLine("[ OK ] connection checked!");

            LogWriteLine("[INFO] starting adb...");

            _ = await DevicesListUpdate();

            SyncDevice();

            _ = Invoke((MethodInvoker)delegate
            {
                Enabled = true;
                waitingForm.Close();
            });
        }

        private void checkBoxSelectAll_CheckedChanged(object sender, EventArgs e)
        {
            if (checkBoxSelectAll.Checked)
            {
                for (int i = 0; i < checkedListBoxApp.Items.Count; i++)
                {
                    checkedListBoxApp.SetItemCheckState(i, CheckState.Checked);
                }
            }
            else
            {
                for (int i = 0; i < checkedListBoxApp.Items.Count; i++)
                {
                    checkedListBoxApp.SetItemCheckState(i, CheckState.Unchecked);
                }
            }
            updateAppCount();
        }

        private void textBoxSearch_TextChanged(object sender, EventArgs e)
        {
            string text = textBoxSearch.Text.ToLowerInvariant();

            checkedListBoxApp.Items.Clear();
            updateAppCount();
            checkBoxSelectAll.Checked = false;

            foreach (string str in ATA.CurrentDeviceSelected.AppsString)
            {
                if (str.Contains(text))
                {
                    _ = checkedListBoxApp.Items.Add(str);
                }
            }
        }

        private void updateAppCount()
        {
            labelSelectedAppCount.Text = "Selected App: " + checkedListBoxApp.CheckedItems.Count;
        }

        private void buttonConnectToIP_Click(object sender, EventArgs e)
        {
            connectToIp();
        }

        private void connectToIp()
        {
            if (comboBoxIP.Text.Length > 0)
            {
                if (!backgroundWorkerADBConnect.IsBusy)
                {
                    LogWriteLine("[INFO] trying to connect to your device...");
                    backgroundWorkerADBConnect.RunWorkerAsync();
                }
                else
                {
                    MessageShowBox("Wait, process still running", 1);
                }
            }
            else
            {
                MessageShowBox("You have to enter an IP", 1);
            }
        }

        private void buttonDisconnectIP_Click(object sender, EventArgs e)
        {
            if (!backgroundWorkerADBDisconnect.IsBusy)
            {
                backgroundWorkerADBDisconnect.RunWorkerAsync();
            }
            else
            {
                MessageShowBox("Wait, process still running", 1);
            }
        }

        private void appFunc(string command1, string command2, int type)
        {
            if (checkedListBoxApp.CheckedItems.Count > 0)
            {
                foreach (object current in checkedListBoxApp.CheckedItems)
                {
                    string log;
                    if ((log = ConsoleProcess.adbFastbootCommandR(" -s " + ATA.CurrentDeviceSelected.ID + " " + command1 + "--user " + ATA.CurrentDeviceSelected.User + " " + current + command2, 0)) != null)
                    {
                        if (type == 1)
                        {
                            ScrollableMessageBox.show(log, "Granted permissions");
                            continue;
                        }
                        LogWriteLine("[ OK ] the command has been injected");
                    }
                    else
                    {
                        LogWriteLine("[ERROR] the command injection has failed");
                    }
                }
            }
            else
            {
                MessageShowBox("No app selected", 1);
            }
        }

        private void buttonFlashZip_Click(object sender, EventArgs e)
        {
            if (!backgroundWorkerZip.IsBusy)
            {
                if (textBoxDirFile.Text.Contains(".zip"))
                {
                    backgroundWorkerZip.RunWorkerAsync();
                }
                else
                {
                    MessageShowBox("No zip selected", 1);
                }
            }
            else
            {
                MessageShowBox("Wait, process still running", 1);
            }
        }

        private void backgroundWorkerZip_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            string fileName = textBoxDirFile.Text[(textBoxDirFile.Text.LastIndexOf('\\') + 1)..];
            LogWriteLine("[ OK ] flashing " + fileName + "...");
            string log = ConsoleProcess.adbFastbootCommandR(commandAssemblerF(string.Format("sideload \"{0}\"", textBoxDirFile.Text)), 0);
            if (log.ToLower().Contains("error") || log.ToLower().Contains("failed") || log.Trim() == "")
            {
                LogWriteLine("[ERROR] " + fileName + " failed to flash, try restarting the sideload process or unplugging and replugging the device to resolve the issue.");
            }
            else
            {
                LogWriteLine("[ OK ] " + fileName + " flashed");
            }
        }

        private void buttonSearchFileFastboot_Click(object sender, EventArgs e)
        {
            openFileDialogZip.Filter = "Img files *.img|*.img;";
            openFileDialogZip.FileName = "";
            openFileDialogZip.Multiselect = false;
            openFileDialogZip.Title = "Select Img";

            DialogResult dr = openFileDialogZip.ShowDialog();
            if (dr == DialogResult.OK)
            {
                textBoxDirImg.Text = openFileDialogZip.FileName;
            }
        }

        private void flash(int index)
        {
            try
            {
                backgroundWorkerFlashImg.RunWorkerAsync(index);
            }
            catch (Exception ex)
            {
                MessageShowBox(ex.ToString(), 0);
            }
        }

        private void backgroundWorkerFlashImg_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            string command;
            string log;

            switch (sender.ToString())
            {
                case "0":
                    command = "flash boot ";
                    break;
                case "1":
                    command = "flash bootloader ";
                    break;
                case "2":
                    command = "flash cache ";
                    break;
                case "3":
                    command = "flash radio ";
                    break;
                case "4":
                    command = "flash recovery ";
                    break;
                case "5":
                    command = "-w && fastboot update ";
                    break;
                case "6":
                    command = "flash system ";
                    break;
                case "7":
                    command = "flash vendor ";
                    break;
                default:
                    return;
            }

            if ((log = ConsoleProcess.adbFastbootCommandR(commandAssemblerF(command + textBoxDirImg.Text), 1)) != null)
            {
                LogWriteLine("[INFO] " + log);
            }
            else
            {
                MessageShowBox("Something went wrong!", 0);
            }
        }

        private async Task<bool> checkAdbFastboot(bool isAdb)
        {
            string exeTmp = isAdb ? "adb.exe" : "fastboot.exe";
            bool exist = File.Exists(exeTmp) && File.Exists("AdbWinUsbApi.dll") && File.Exists("AdbWinApi.dll");

            if (!exist)
            {
                LogWriteLine("[ERROR] adb not found");
            }

            return exist || await ADBDownload();
        }

        private void buttonRebootToSystem_Click(object sender, EventArgs e)
        {
            LogWriteLine("[INFO] " + ConsoleProcess.adbFastbootCommandR(commandAssemblerF("reboot"), 1));
            Thread.Sleep(1000);
            reloadList();
        }

        private void buttonHardReset_Click(object sender, EventArgs e)
        {
            DialogResult dialogResult = MessageBox.Show("Do you want to erase your phone?", "Erase Phone", MessageBoxButtons.YesNo);
            if (dialogResult == DialogResult.Yes)
            {
                LogWriteLine("[INFO] erasing process started...");
                _ = ConsoleProcess.systemCommand(commandAssembler(false, "fastboot erase userdata && fastboot erase cache")); ;
                LogWriteLine("[INFO] erasing process finished!!");
            }
        }

        private void rebootSmartphone()
        {
            LogWriteLine("[INFO] rebooting smartphone...");
            _ = ConsoleProcess.systemCommand("adb -s " + ATA.CurrentDeviceSelected.ID + " reboot");
            LogWriteLine("[INFO] smartphone rebooted");
        }

        private void buttonRebootRecovery_Click(object sender, EventArgs e)
        {
            LogWriteLine(ConsoleProcess.adbFastbootCommandR(commandAssemblerF("reboot recovery"), 1));
            Thread.Sleep(1000);
            reloadList();
        }

        private void buttonBootloaderMenu_Click(object sender, EventArgs e)
        {
            BootloaderMenu bootloaderMenu = new();
            _ = bootloaderMenu.ShowDialog();
        }

        private void toolStripButton1_Click(object sender, EventArgs e)
        {
            loadApps(ATA.CurrentDeviceSelected.AppMode);
        }

        public void uninstaller(CheckedListBox.CheckedItemCollection foundPackageList)
        {
            string command = "-s " + ATA.CurrentDeviceSelected.ID + " shell pm uninstall -k --user " + ATA.CurrentDeviceSelected.User + " ";
            LoadingForm load;
            List<string> arrayApkSelect = new();
            foreach (object list in foundPackageList)
            {
                arrayApkSelect.Add(list.ToString());
            }
            load = new LoadingForm(arrayApkSelect, command, "Uninstalled:");
            _ = load.ShowDialog();
            if (load.DialogResult != DialogResult.OK)
            {
                MessageShowBox("Error during uninstallation process", 0);
            }
            loadApps(ATA.CurrentDeviceSelected.AppMode);
            checkBoxSelectAll.Checked = false;
        }

        private void toolStripButton4_Click(object sender, EventArgs e)
        {
            if (checkedListBoxApp.CheckedItems.Count > 0)
            {
                uninstaller(checkedListBoxApp.CheckedItems);
            }
            else
            {
                MessageShowBox("No app selected", 1);
            }
        }

        private void toolStripButton5_Click(object sender, EventArgs e)
        {
            if (checkedListBoxApp.CheckedItems.Count > 0)
            {
                List<string> arrayApkSelect = new();
                foreach (object list in checkedListBoxApp.CheckedItems)
                {
                    arrayApkSelect.Add(list.ToString());
                }

                PackageMenu packageMenu = new(arrayApkSelect);
                _ = packageMenu.ShowDialog();
                string command;
                string commandName;
                switch (packageMenu.DialogResult1)
                {
                    case 1:
                        command = commandAssemblerF("shell pm enable --user " + ATA.CurrentDeviceSelected.User + " ");
                        commandName = "Enabled:";
                        break;
                    case 0:
                        command = commandAssemblerF("shell pm disable-user --user " + ATA.CurrentDeviceSelected.User + " ");
                        commandName = "Disabled:";
                        break;
                    case 2:
                        command = commandAssemblerF("shell pm clear --user " + ATA.CurrentDeviceSelected.User + " ");
                        commandName = "Cleared:";
                        break;
                    case -1:
                        LogWriteLine("[ERROR] operations canceled");
                        return;
                    default:
                        MessageShowBox("Generic error", 0);
                        return;
                }
                loadMethod(arrayApkSelect, command, commandName);
            }
            else
            {
                MessageShowBox("No app selected", 1);
            }
        }

        public void loadMethod(List<string> arrayApkSelect, string command, string commandName)
        {
            LoadingForm load = new(arrayApkSelect, command, commandName);
            _ = load.ShowDialog();
            if (load.DialogResult == DialogResult.OK)
            {
                loadApps(ATA.CurrentDeviceSelected.AppMode);
            }
            else
            {
                MessageShowBox("Error during this process", 0);
            }
        }

        private void nonSystemAppToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            toolStripButtonUninstallApp.Enabled = true;
            toolStripButtonRestoreApp.Enabled = false;
            toolStripButtonPermissionMenu.Enabled = true;
            toolStripButtonExtract.Enabled = true;
            toolStripButtonPackageManager.Enabled = true;
            loadApps(AppMode.NONSYSTEM);
        }

        private void systemAppToolStripMenuItem1_Click(object sender, EventArgs e)
        {
            toolStripButtonUninstallApp.Enabled = true;
            toolStripButtonExtract.Enabled = true;
            toolStripButtonRestoreApp.Enabled = false;
            toolStripButtonPermissionMenu.Enabled = true;
            toolStripButtonPackageManager.Enabled = true;

            loadApps(AppMode.SYSTEM);
        }

        private void allToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toolStripButtonUninstallApp.Enabled = true;
            toolStripButtonRestoreApp.Enabled = false;
            toolStripButtonExtract.Enabled = true;
            toolStripButtonPermissionMenu.Enabled = true;
            toolStripButtonPackageManager.Enabled = true;

            loadApps(AppMode.ALL);
        }

        private void toolStripButtonFilter_Click(object sender, EventArgs e)
        {
            contextMenuStripFilterBy.Show(Cursor.Position);
        }

        private void textBoxSearch_Click(object sender, EventArgs e)
        {
            textBoxSearch.SelectAll();
        }

        private void toolStripButtonPermissionMenu_Click(object sender, EventArgs e)
        {
            contextMenuStripPermissionMenu.Show(Cursor.Position);
        }

        private void groupBox6_DragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                backgroundWorkerAPKinstall.RunWorkerAsync((string[])e.Data.GetData(DataFormats.FileDrop));
            }
        }

        private void groupBox6_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void checkedListBoxApp_SelectedIndexChanged(object sender, EventArgs e)
        {
            updateAppCount();
        }

        private void checkGrantedPermissionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell dumpsys package --user " + ATA.CurrentDeviceSelected.User, null, 1);
        }

        private void comboBoxDevices_SelectedIndexChanged(object sender, EventArgs e)
        {
            ATA.CurrentDeviceSelected = ata.Devices[comboBoxDevices.SelectedIndex];

            disableEnableSystem(false);
            panelFastboot.Enabled = false;
            panelRecovery.Enabled = false;
        }

        private void buttonReloadDevicesList_Click(object sender, EventArgs e)
        {
            reloadList();
        }

        private async void reloadList()
        {
            Invoke(delegate
            {
                comboBoxDevices.Items.Clear();
            });
            ata.Devices.Clear();
            _ = await DevicesListUpdate();
        }

        private void tabControl1_Selected(object sender, TabControlEventArgs e)
        {
            Tab oldTabTmp = ata.CurrentTab;
            ata.setCurrentTab(tabControls.SelectedTab.Text);

            if (ata.CurrentTab != oldTabTmp)
            {
                ata.Devices.Clear();
                comboBoxDevices.Items.Clear();
                buttonDeviceLogs.Enabled = false;
                buttonTaskManager.Enabled = false;
                buttonMobileScreenShare.Enabled = false;
                disableEnableSystem(false);
            }

            if (ata.CurrentTab != Tab.SYSTEM)
            {
                buttonSyncApp.Enabled = false;
            }

            panelRecovery.Enabled = false;
            panelFastboot.Enabled = false;
        }

        private async Task<bool> ADBDownload()
        {
            if (!ata.IsConnected)
            {
                MessageShowBox("You are offline, ATA can not download ADB", 0);
                return false;
            }
            Invoke(delegate
            {
                disableEnableSystem(false);
                buttonDisconnectIP.Enabled = false;
            });

            ExeMissingForm adbError = new("adb.exe not found\n\nDo you want to download sdk platform tool?\n\n[By pressing YES you agree sdk platform tool terms and conditions]\nfor more info press info button", "Error, ADB Missing!");
            _ = adbError.ShowDialog();

            switch (adbError.DialogResult)
            {
                case DialogResult.Yes:
                    LogWriteLine("[INFO] downloading sdk platform tool...");
                    using (HttpClient client = new())
                    {
                        try
                        {
                            string url = "https://dl.google.com/android/repository/platform-tools-latest-windows.zip";
                            string filePath = "sdkplatformtool.zip";
                            using (HttpResponseMessage response = await client.GetAsync(url))
                            {
                                _ = response.EnsureSuccessStatusCode();
                                using FileStream fs = new(filePath, FileMode.Create);
                                await response.Content.CopyToAsync(fs);
                            }
                            LogWriteLine("[INFO] sdk platform tool downloaded!");
                            LogWriteLine("[INFO] unzipping sdk platform tool...");
                            if (Directory.Exists("platform-tools"))
                            {
                                Directory.Delete("platform-tools", true);
                            }
                            using (ZipFile zip = ZipFile.Read("sdkplatformtool.zip"))
                            {
                                zip.ExtractAll(Path.GetDirectoryName(Application.ExecutablePath));
                            }
                            LogWriteLine("[ OK ] sdk platform tool extraced!");
                            LogWriteLine("[INFO] getting things ready...");
                            _ = ConsoleProcess.systemCommand("taskkill /f /im adb.exe");
                            _ = ConsoleProcess.systemCommand("taskkill /f /im fastboot.exe");
                            _ = ConsoleProcess.systemCommand("del adb.exe");
                            _ = ConsoleProcess.systemCommand("del AdbWinUsbApi.dll");
                            _ = ConsoleProcess.systemCommand("del AdbWinApi.dll");
                            _ = ConsoleProcess.systemCommand("del fastboot.exe");
                            _ = ConsoleProcess.systemCommand("move platform-tools\\adb.exe \"%cd%\"");
                            _ = ConsoleProcess.systemCommand("move platform-tools\\AdbWinUsbApi.dll \"%cd%\"");
                            _ = ConsoleProcess.systemCommand("move platform-tools\\AdbWinApi.dll \"%cd%\"");
                            _ = ConsoleProcess.systemCommand("move platform-tools\\fastboot.exe \"%cd%\"");
                            Directory.Delete("platform-tools", true);
                            File.Delete("sdkplatformtool.zip");

                            if (!(File.Exists("adb.exe") && File.Exists("AdbWinUsbApi.dll") && File.Exists("AdbWinApi.dll") && File.Exists("fastboot.exe")))
                            {
                                throw new Exception();
                            }

                            LogWriteLine("[ OK ] ATA ready!");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            LogWriteLine("[ERROR] an error occurred while attempting to download the SDK Platform Tools");
                            MessageShowBox("An error occurred while attempting to download the SDK Platform Tools\nError message:" + ex, 0);
                            disableSystem(true);
                            return false;
                        }
                    }
                case DialogResult.No:
                    disableSystem(true);
                    break;
                case DialogResult.OK:
                    ConsoleProcess.openLink("https://developer.android.com/license");
                    disableSystem(true);
                    break;
                default:
                    disableSystem(true);
                    break;
            }

            return false;
        }

        private void disableSystem(bool a)
        {
            Invoke(delegate
            {
                tabPageSystem.Enabled = !a;
                buttonMobileScreenShare.Enabled = !a;
            });
        }

        private void toolStripButtonRestoreApp_Click(object sender, EventArgs e)
        {
            if (checkedListBoxApp.CheckedItems.Count > 0)
            {
                List<string> apps = new();
                foreach (object app in checkedListBoxApp.CheckedItems)
                {
                    apps.Add(app.ToString());
                }
                LoadingForm load = new(apps, commandAssemblerF("shell cmd package install-existing --user " + ATA.CurrentDeviceSelected.User + " "), "Restored:");
                _ = load.ShowDialog();
                if (load.DialogResult != DialogResult.OK)
                {
                    MessageShowBox("Error during restoring process", 0);
                }
                loadApps(ATA.CurrentDeviceSelected.AppMode);
            }
            else
            {
                MessageShowBox("No app selected", 1);
            }
        }

        private void uninstalledAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toolStripButtonUninstallApp.Enabled = false;
            toolStripButtonRestoreApp.Enabled = true;
            toolStripButtonPermissionMenu.Enabled = false;
            toolStripButtonPackageManager.Enabled = false;
            toolStripButtonExtract.Enabled = false;
            loadApps(AppMode.UNINSTALLED);
        }

        private void pictureBoxLogo_Click(object sender, EventArgs e)
        {
            ConsoleProcess.openLink("https://github.com/MassimilianoSartore/ATA-GUI");
        }

        private void buttonMobileScreenShare_Click(object sender, EventArgs e)
        {
            if (File.Exists("scrcpy.exe"))
            {
                _ = ConsoleProcess.scrcpyProcess("-s " + ATA.CurrentDeviceSelected.ID);
            }
            else
            {
                try
                {
                    backgroundWorkerExeDownloader.RunWorkerAsync();
                }
                catch (Exception ex)
                {
                    MessageShowBox(ex.Message, 0);
                }
            }
        }

        private async void backgroundWorkerExeDownloader_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            if (!ata.IsConnected)
            {
                MessageShowBox("You are offline, ATA can not download scrcpy", 0);
                return;
            }
            ExeMissingForm scrcpyError = new("scrcpy.exe not found\n\nDo you want to download scrcpy?\n\n[By pressing YES you agree scrcpy terms and conditions]\nfor more info press info button", "Error, scrcpy Missing!");
            _ = scrcpyError.ShowDialog();
            switch (scrcpyError.DialogResult)
            {
                case DialogResult.Yes:
                    LogWriteLine("[INFO] downloading scrcpy...");
                    using (HttpClient client = new())
                    {
                        try
                        {
                            string url = "https://ata.msartore.dev/scrcpy/download";
                            string filePath = "scrcpy.zip";
                            using (HttpResponseMessage response = await client.GetAsync(url))
                            {
                                _ = response.EnsureSuccessStatusCode();
                                using FileStream fs = new(filePath, FileMode.Create);
                                await response.Content.CopyToAsync(fs);
                            }
                            LogWriteLine("[ OK ] scrcpy downloaded");
                            LogWriteLine("[INFO] unzipping scrcpy...");
                            using (ZipFile zip = ZipFile.Read("scrcpy.zip"))
                            {
                                zip.ExtractAll(Path.GetDirectoryName(Application.ExecutablePath), ExtractExistingFileAction.DoNotOverwrite);
                            }
                            string directory = Directory.GetDirectories(Path.GetDirectoryName(Application.ExecutablePath)).Where(it => it.Contains("scrcpy-win64")).FirstOrDefault();

                            if (directory != null)
                            {
                                foreach (string item in Directory.EnumerateFiles(directory))
                                {
                                    string name = item[(item.LastIndexOf('\\') + 1)..];
                                    if (!File.Exists(name))
                                    {
                                        File.Copy(item, Path.GetDirectoryName(Application.ExecutablePath) + "\\" + name, false);
                                    }
                                }
                                LogWriteLine("[ OK ] scrcpy extracted");
                                LogWriteLine("[INFO] getting things ready...");
                                Directory.Delete(directory, true);
                                File.Delete("scrcpy.zip");
                                LogWriteLine("[ OK ] scrcpy ready!");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageShowBox(ex.Message, 0);
                            disableSystem(true);
                        }
                    }
                    break;
                case DialogResult.No:
                    break;
                case DialogResult.OK:
                    ConsoleProcess.openLink("https://raw.githubusercontent.com/Genymobile/scrcpy/master/LICENSE");
                    break;
                default:
                    break;
            }
        }

        private void labelHelp_MouseLeave(object sender, EventArgs e)
        {
            labelHelp.BackColor = Color.Black;
        }

        private void labelSettings_MouseLeave(object sender, EventArgs e)
        {
            labelSettings.BackColor = Color.Black;
        }

        private void labelSettings_Click(object sender, EventArgs e)
        {
            Settings settings = new();
            _ = settings.ShowDialog();
        }

        private void labelHelp_Click(object sender, EventArgs e)
        {
            contextMenuStripHelp.Show(Cursor.Position);
        }

        private void videoTutorialToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ConsoleProcess.openLink("https://github.com/MassimilianoSartore/ATA-GUI/wiki#coming-soon");
        }

        private void pictureBoxMinimize_Click(object sender, EventArgs e)
        {
            WindowState = FormWindowState.Minimized;
        }

        private void pictureBoxClose_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private void closeATA()
        {
            if (Process.GetProcessesByName("adb").Length != 0)
            {
                DialogResult dialogResult = MessageBox.Show("Do you want to kill ADB?", "Kill ADB", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (dialogResult == DialogResult.Yes)
                {
                    _ = ConsoleProcess.adbFastbootCommandR("kill-server", 0);
                }
            }
        }

        private void pictureBoxMinimize_MouseEnter(object sender, EventArgs e)
        {
            pictureBoxMinimize.BackColor = System.Drawing.ColorTranslator.FromHtml("#1f2121");
        }

        private void pictureBoxMinimize_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxMinimize.BackColor = Color.Black;
        }

        private void pictureBoxClose_MouseEnter(object sender, EventArgs e)
        {
            pictureBoxClose.BackColor = Color.Red;
        }

        private void pictureBoxClose_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxClose.BackColor = Color.Black;
        }

        private void buttonDeviceLogs_Click(object sender, EventArgs e)
        {
            DeviceLogs deviceLog = new(ATA.CurrentDeviceSelected.ID);
            deviceLog.Show();
        }

        private void submitFeedbackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Feedback feedback = new();
            _ = feedback.ShowDialog();
        }

        private void grantWriteSecureSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm grant ", " android.permission.WRITE_SECURE_SETTINGS", 0);
        }

        private void revokeWriteSecureSettingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm revoke ", " android.permission.WRITE_SECURE_SETTINGS", 0);
        }

        private void grantDUMPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm grant ", " android.permission.DUMP", 0);
        }

        private void revokeDUMPToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm revoke ", " android.permission.DUMP", 0);
        }

        private void toolStripButtonSearch_Click(object sender, EventArgs e)
        {
            if (checkedListBoxApp.CheckedItems.Count == 0)
            {
                MessageShowBox("No app selected", 1);
                return;
            }
            contextMenuStripSearch.Show(Cursor.Position);
        }

        private void duckduckgoToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (string file in checkedListBoxApp.CheckedItems)
            {
                ConsoleProcess.openLink("https://duckduckgo.com/?q=" + file);
            }
        }

        private void googleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (string file in checkedListBoxApp.CheckedItems)
            {
                ConsoleProcess.openLink("https://www.google.com/search?q=" + file);
            }
        }

        private void playMarketToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (string file in checkedListBoxApp.CheckedItems)
            {
                ConsoleProcess.openLink("https://play.google.com/store/apps/details?id=" + file);
            }
        }

        private void APKMirrorToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (string file in checkedListBoxApp.CheckedItems)
            {
                ConsoleProcess.openLink("https://www.apkmirror.com/?post_type=app_release&searchtype=apk&s=" + file);
            }
        }

        private void fDroidToolStripMenuItem_Click(object sender, EventArgs e)
        {
            foreach (string file in checkedListBoxApp.CheckedItems)
            {
                ConsoleProcess.openLink("https://f-droid.org/en/packages/" + file);
            }
        }

        private void disabledAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            toolStripButtonUninstallApp.Enabled = false;
            toolStripButtonRestoreApp.Enabled = false;
            toolStripButtonPermissionMenu.Enabled = true;
            toolStripButtonPackageManager.Enabled = true;
            toolStripButtonExtract.Enabled = false;

            loadApps(AppMode.DISABLE);
        }

        private void toolStripMenuItemADBKill_Click(object sender, EventArgs e)
        {
            _ = ConsoleProcess.systemCommand("taskkill /f /im " + ata.FILEADB);
            MessageShowBox("Adb.exe killed!", 2);
        }

        private void labelTools_MouseEnter(object sender, EventArgs e)
        {
            labelTools.BackColor = ColorTranslator.FromHtml("#1f2121");
        }

        private void labelTools_MouseLeave(object sender, EventArgs e)
        {
            labelTools.BackColor = Color.Black;
        }

        private void labelHelp_MouseEnter(object sender, EventArgs e)
        {
            labelHelp.BackColor = ColorTranslator.FromHtml("#1f2121");
        }

        private void labelSettings_MouseEnter(object sender, EventArgs e)
        {
            labelSettings.BackColor = ColorTranslator.FromHtml("#1f2121");
        }

        private void labelTools_Click(object sender, EventArgs e)
        {
            contextMenuStripTools.Show(Cursor.Position);
        }

        private void buttonTaskManager_Click(object sender, EventArgs e)
        {
            _ = new TaskManager(ATA.CurrentDeviceSelected.AppsString).ShowDialog();
        }

        private void backgroundWorkerADBConnect_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            int deviceCountTmp = ata.Devices.Count;
            string ip = string.Empty;
            string port = textBoxPort.Text.Trim();

            Invoke(delegate
            {
                ip = comboBoxIP.Text.Trim();
            });

            if (ip.Length > 1)
            {
                if (ATA.CurrentDeviceSelected != null)
                {
                    if (ATA.CurrentDeviceSelected.ID.Length > 0)
                    {
                        _ = ConsoleProcess.adbFastbootCommandR(" -s " + ATA.CurrentDeviceSelected.ID + " tcpip " + port, 0);
                    }
                }

                string result = ConsoleProcess.adbFastbootCommandR("connect " + ip + ":" + port, 0);

                if (!result.Contains("cannot connect"))
                {
                    if (result.Contains("already"))
                    {
                        MessageShowBox("Already connected to " + ip, 2);
                    }
                    else
                    {
                        Invoke(delegate
                        {
                            if (!ata.IPList.Contains(ip))
                            {
                                _ = ata.IPList.Add(ip);
                                _ = comboBoxIP.Items.Add(ip);

                                using StreamWriter writer = new(ATA.IPFileName, true);
                                writer.WriteLine(ip);
                            }
                            buttonConnectToIP.Enabled = false;
                            buttonDisconnectIP.Enabled = true;
                        });

                        MessageShowBox("Connected to " + ip, 2);

                        Thread.Sleep(1000);

                        reloadList();
                    }
                }
                else
                {
                    MessageShowBox(string.Format("Failed to connect to {0}\nError message: {1}", ip, result), 0);
                }
            }
        }

        private void backgroundWorkerADBDisconnect_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            int deviceCountTmp = ata.Devices.Count;
            string ip = string.Empty;

            Invoke(delegate
            {
                ip = comboBoxIP.Text.Trim();
            });

            if (ip.Length > 1)
            {
                bool result = ConsoleProcess.systemCommand("adb disconnect " + ip).Contains("disconnected");

                if (result)
                {
                    MessageShowBox(ip + " disconnected", 2);
                    Invoke(delegate
                    {
                        buttonConnectToIP.Enabled = true;
                        buttonDisconnectIP.Enabled = false;
                    });

                    reloadList();

                    SyncDevice();
                }
                else
                {
                    MessageShowBox(ip + " failed to disconnect", 0);
                }
            }
        }

        private void installAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            installApp("");
        }

        private void downgradeAppToolStripMenuItem_Click(object sender, EventArgs e)
        {
            installApp("-d");
        }

        private void installApp(string command)
        {
            openFileDialogAPK.Filter = "Apk files *.Apk|*.apk;";
            openFileDialogAPK.FileName = "";
            openFileDialogAPK.Multiselect = true;
            openFileDialogAPK.Title = "Select Apk";

            DialogResult dr = openFileDialogAPK.ShowDialog();

            if (dr == DialogResult.OK)
            {
                backgroundWorkerAPKinstall.RunWorkerAsync(openFileDialogAPK.FileNames);
            }
        }

        private void textBoxIP_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                connectToIp();
            }
        }

        private void grantSYSTEMALERTWINDOWToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm grant ", " android.permission.SYSTEM_ALERT_WINDOW", 0);
        }

        private void revokeSYSTEMALERTWINDOWToolStripMenuItem_Click(object sender, EventArgs e)
        {
            appFunc("shell pm revoke ", " android.permission.SYSTEM_ALERT_WINDOW", 0);
        }

        private void toolStripButtonSetDefault_Click(object sender, EventArgs e)
        {
            CheckedListBox.CheckedItemCollection checkedItems = checkedListBoxApp.CheckedItems;

            switch (checkedItems.Count)
            {
                case 0:
                    MessageShowBox("No app seleted", 1);
                    break;
                case 1:
                    foreach (object current in checkedItems)
                    {
                        _ = new DefaultApp(current.ToString()).ShowDialog();
                    }
                    break;
                default:
                    MessageShowBox("Too much app seleted", 1);
                    break;
            }
        }

        private void comboBoxIP_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == (char)13)
            {
                connectToIp();
            }
        }

        private void comboBoxIP_TextUpdate(object sender, EventArgs e)
        {
            buttonConnectToIP.Enabled = buttonDisconnectIP.Enabled = true;
        }

        private void toolStripButtonExtract_Click(object sender, EventArgs e)
        {
            if (checkedListBoxApp.CheckedItems.Count > 0)
            {
                List<string> arrayApkSelect = new();
                foreach (object list in checkedListBoxApp.CheckedItems)
                {
                    arrayApkSelect.Add(list.ToString());
                }
                LoadingForm loadingForm = new(ATA.CurrentDeviceSelected.ID, arrayApkSelect);
                _ = loadingForm.ShowDialog();
                for (int i = 0; i < checkedListBoxApp.Items.Count; i++)
                {
                    checkedListBoxApp.SetItemChecked(i, false);
                }
            }
            else
            {
                MessageShowBox("No app selected!", 1);
            }
        }

        private void buttonTurnOffAdb_Click(object sender, EventArgs e)
        {
            _ = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + " shell settings put global adb_enabled 0", 0);
            LogWriteLine("[INFO] the command has been ejected");
            SyncDevice();
        }

        private void richTextBoxLog_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            _ = Process.Start(e.LinkText);
        }

        private void buttonUnlockButtons_Click(object sender, EventArgs e)
        {
            textBoxPort.Enabled = true;
            buttonConnectToIP.Enabled = true;
            buttonDisconnectIP.Enabled = true;
        }

        private void buttonSetRotation_Click(object sender, EventArgs e)
        {
            string commandRun = " shell wm set-ignore-orientation-request ";

            if (domainUpDownFreeRotation.Items.Count > 1)
            {
                commandRun += " -d " + domainUpDownFreeRotation.Text;
            }

            commandRun += (!ATA.CurrentDeviceSelected.IsRotationFreeEnabled).ToString().ToLowerInvariant();

            _ = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + commandRun, 0);
            ATA.CurrentDeviceSelected.IsRotationFreeEnabled = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + " shell wm get-ignore-orientation-request", 0).Contains("true");
            LogWriteLine("[INFO] free rotation " + buttonSetRotation.Text.ToLowerInvariant() + "ed");
            buttonSetRotation.Text = ATA.CurrentDeviceSelected.IsRotationFreeEnabled ? "Unset" : "Set";
        }

        private void buttonCommandInject_Click(object sender, EventArgs e)
        {
            if (richTextBoxTerminal.Text.Trim().Length == 0)
            {
                MessageShowBox("You have to enter a command!", 1);
            }
            else
            {
                LogWriteLine("[INFO] " + ConsoleProcess.adbFastbootCommandR(richTextBoxTerminal.Text.Trim(), radioButtonADB.Checked ? 0 : 1));
            }
        }

        private void buttonInjectText_Click(object sender, EventArgs e)
        {
            _ = ConsoleProcess.adbFastbootCommandR("-s " + ATA.CurrentDeviceSelected.ID + " shell input text \"" + richTextBoxSend.Text + "\"", 0);
            if (richTextBoxSend.Text.Length == 0)
            {
                MessageShowBox("You have to enter a text to inject!", 1);
            }
            else
            {
                LogWriteLine("[ OK ] text injected");
            }
        }

        private void buttonClearTextSend_Click(object sender, EventArgs e)
        {
            richTextBoxSend.Clear();
        }

        private void pictureBoxMaximize_Click(object sender, EventArgs e)
        {
            maximizeWindow();
        }

        private void pictureBoxMaximize_MouseEnter(object sender, EventArgs e)
        {
            pictureBoxMaximize.BackColor = ColorTranslator.FromHtml("#1f2121");
        }

        private void pictureBoxMaximize_MouseLeave(object sender, EventArgs e)
        {
            pictureBoxMaximize.BackColor = Color.Black;
        }

        private void pictureBoxMaximize_MouseClick(object sender, MouseEventArgs e)
        {
            pictureBoxMaximize.Image = ata.IsMaximize ? Properties.Resources.icons8_restore_down_16 : Properties.Resources.icons8_maximize_button_16;
        }

        private void panelTopBar_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            maximizeWindow();
        }

        private void maximizeWindow()
        {
            if (ata.IsMaximize)
            {
                Size = ata.windowSize;
                CenterToScreen();
            }
            else
            {
                Size = new Size(Screen.GetWorkingArea(this).Width, Screen.GetWorkingArea(this).Height);
                Location = Screen.GetWorkingArea(this).Location;
            }
            ata.IsMaximize = !ata.IsMaximize;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (textBoxDirImg.Text.Length > 0)
            {
                if (radioButtonBoot.Checked)
                {
                    flash(0);
                }
                if (radioButtonBootloader.Checked)
                {
                    flash(1);
                }
                if (radioButtonCache.Checked)
                {
                    flash(2);
                }
                if (radioButtonRadio.Checked)
                {
                    flash(3);
                }
                if (radioButtonRecovery.Checked)
                {
                    flash(4);
                }
                if (radioButtonRom.Checked)
                {
                    flash(5);
                }
                if (radioButtonSystem.Checked)
                {
                    flash(6);
                }
                if (radioButtonVendor.Checked)
                {
                    flash(7);
                }
            }
            else
            {
                MessageShowBox("Please select a img file", 1);
            }
        }

        private void buttonBrowseFile_Click(object sender, EventArgs e)
        {
            openFileDialogZip.Filter = "Zip files *.zip|*.zip;";
            openFileDialogZip.FileName = "";
            openFileDialogZip.Multiselect = false;
            openFileDialogZip.Title = "Select Zip";

            DialogResult dr = openFileDialogZip.ShowDialog();
            if (dr == DialogResult.OK)
            {
                textBoxDirFile.Text = openFileDialogZip.FileName;
            }
        }

        private void backgroundWorkerAPKinstall_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            APK.installApk((string[])e.Argument, ATA.CurrentDeviceSelected.User, (name) =>
            {
                Invoke(delegate
                {
                    LogWriteLine("[INFO] installing " + name + " ...");
                });
            });
            loadApps(ATA.CurrentDeviceSelected.AppMode);
        }

        private void backgroundWorkerFileTransfer_DoWork(object sender, System.ComponentModel.DoWorkEventArgs e)
        {
            if (((DragEventArgs)e.Argument).Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] filePaths = (string[])((DragEventArgs)e.Argument).Data.GetData(DataFormats.FileDrop);
                LoadingForm loading = new(filePaths.ToList(), ATA.CurrentDeviceSelected.ID);
                _ = loading.ShowDialog();
            }
        }

        private void buttonrr__Click(object sender, EventArgs e)
        {
            _ = ConsoleProcess.systemCommand(commandAssembler(true, "reboot recovery"));
            LogWriteLine("[INFO] rebooted");
            reloadList();
        }

        private void buttonrs__Click(object sender, EventArgs e)
        {
            rebootSmartphone();
            LogWriteLine("[INFO] rebooted");
            reloadList();
        }

        private void buttonrf__Click(object sender, EventArgs e)
        {
            _ = ConsoleProcess.systemCommand(commandAssembler(true, "reboot-bootloader"));
            LogWriteLine("[INFO] rebooted");
            reloadList();
        }

        private void buttonCamera_Click(object sender, EventArgs e)
        {
            if (ATA.CurrentDeviceSelected.Version > 11)
            {
                try
                {
                    if (File.Exists("scrcpy.exe"))
                    {
                        string[] result = ConsoleProcess.scrcpyProcess("--version").Split(' ');
                        foreach (string item in result)
                        {
                            if (item.Contains("."))
                            {
                                string nTmp = "";

                                for (int i = 0; i < item.Length; i++)
                                {
                                    if (char.IsDigit(item[i]))
                                    {
                                        nTmp += item[i];
                                        if (nTmp.Length == 2)
                                        {
                                            break;
                                        }
                                    }
                                }

                                if (nTmp.Length < 0)
                                {
                                    continue;
                                }

                                if (int.Parse(nTmp) < 22)
                                {
                                    MessageShowBox("scrcpy is not up-to-date, update it to use this feature", 1);
                                    return;
                                }
                                else
                                {
                                    break;
                                }
                            }
                        }
                        _ = ConsoleProcess.systemCommandAsync(string.Format("scrcpy -s {0} --video-source=camera --camera-size=1920x1080 --camera-facing={1}", ATA.CurrentDeviceSelected.ID, comboBoxCameraModes.Text));
                    }
                    else
                    {
                        backgroundWorkerExeDownloader.RunWorkerAsync();
                    }
                }
                catch (Exception ex)
                {
                    MessageShowBox(ex.Message, 0);
                }
            }
            else
            {
                MessageShowBox("This device does not support camera mirroring", 0);
            }
        }

        private void buttonBloatwareRemover_Click(object sender, EventArgs e)
        {
            List<string> appsSystem = new();
            List<string> appsNonSystem = new();

            try
            {
                string system = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell pm list packages -s --user " + ATA.CurrentDeviceSelected.User), 0);
                string nonSystem = ConsoleProcess.adbFastbootCommandR(commandAssemblerF("shell pm list packages -3 --user " + ATA.CurrentDeviceSelected.User), 0); ;

                foreach (string line in system.Split("\n"))
                {
                    if (line.Contains("package:"))
                    {
                        appsSystem.Add(line[8..]);
                    }
                }

                foreach (string line in nonSystem.Split("\n"))
                {
                    if (line.Contains("package:"))
                    {
                        appsNonSystem.Add(line[8..]);
                    }
                }

                BloatwareRemover bloatwareDetecter = new(appsNonSystem, appsSystem)
                {
                    CurrentDevice = ATA.CurrentDeviceSelected.ID
                };
                _ = bloatwareDetecter.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageShowBox(ex.Message, 0);
            }
        }

        private void driverToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ScrollableMessageBox.show("Acer \thttps://www.acer.com/worldwide/support/\r\nAlcatel Mobile \thttps://www.alcatelmobile.com/support/\r\nAsus \thttps://www.asus.com/support/Download-Center/\r\nBlackberry \thttps://swdownloads.blackberry.com/Downloads/entry.do?code=4EE0932F46276313B51570F46266A608\r\nDell \thttps://support.dell.com/support/downloads/index.aspx?c=us&cs=19&l=en&s=dhs&~ck=anavml\r\nFCNT \thttps://www.fcnt.com/support/develop/#anc-03\r\nGoogle\thttps://developer.android.com/studio/run/win-usb\r\nHTC \thttps://www.htc.com/support\r\nHuawei \thttps://consumer.huawei.com/en/support/index.htm\r\nIntel \thttps://www.intel.com/software/android\r\nKyocera \thttps://kyoceramobile.com/support/drivers/\r\nLenovo \thttps://support.lenovo.com/us/en/GlobalProductSelector\r\nLGE \thttps://www.lg.com/us/support/software-firmware\r\nMotorola \thttps://motorola-global-portal.custhelp.com/app/answers/detail/a_id/88481/\r\nMTK \thttp://online.mediatek.com/Public%20Documents/MTK_Android_USB_Driver.zip (ZIP download)\r\nSamsung \thttps://developer.samsung.com/galaxy/others/android-usb-driver-for-windows\r\nSharp \thttp://k-tai.sharp.co.jp/support/\r\nSony Mobile Communications \thttps://developer.sonymobile.com/downloads/drivers/\r\nToshiba \thttps://support.toshiba.com/sscontent?docId=4001814\r\nXiaomi \thttps://web.vip.miui.com/page/info/mio/mio/detail?postId=18464849&app_version=dev.20051\r\nZTE \thttp://support.zte.com.cn/support/news/NewsDetail.aspx?newsId=1000442", "OEM drivers");
        }

        private void groupBox2_DragDrop(object sender, DragEventArgs e)
        {
            backgroundWorkerFileTransfer.RunWorkerAsync(e);
        }

        private void groupBox2_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void MainForm_FormClosed(object sender, FormClosedEventArgs e)
        {
            closeATA();
        }
    }
}
