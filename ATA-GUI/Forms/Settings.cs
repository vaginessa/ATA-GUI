﻿using System;
using System.IO;
using System.Windows.Forms;
using ATA_GUI.Classes;
using ATA_GUI.Utils;
using Newtonsoft.Json.Linq;

namespace ATA_GUI
{
    public partial class Settings : Form
    {
        private string changelog = string.Empty;
        private static readonly string CURRENTVERSION = ATA.CURRENTVERSION;
        private bool runningCheck;
        private bool starting = true;

        public Settings()
        {
            InitializeComponent();
        }

        private async void buttonCheckLastVersion_ClickAsync(object sender, EventArgs e)
        {
            if (!runningCheck)
            {
                runningCheck = true;
                if (MainForm.pingCheck())
                {
                    try
                    {
                        labelLog.Text = "Status: Checking for new ATA version...";

                        _ = await ATA.CheckVersion((currentRelease, latestRelease, jsonReal) =>
                        {
                            Invoke(delegate
                            {
                                changelog = jsonReal[0]["body"];

                                if (Utils.Version.CompareVersions(latestRelease, currentRelease) == Utils.Version.VersionComparisonResult.GreaterThan)
                                {
                                    if (MessageBox.Show("New version found: " + latestRelease + "\nCurrent Version: " + CURRENTVERSION + "\n\nDo you want to update it?", "Update found!", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                                    {
                                        ConsoleProcess.openLink((string)jsonReal[0]["html_url"]);
                                        jsonReal[0]["assets"][0].TryGetValue("browser_download_url", out JToken urlDownload);
                                        UpdateForm update = new(urlDownload.ToString());
                                        _ = update.ShowDialog();
                                    }
                                    else
                                    {
                                        labelLog.Text = "Status:\tATA is not up to date, update\nit as soon as you can!";
                                    }
                                }
                                else
                                {
                                    switch (Utils.Version.CompareVersions(currentRelease, latestRelease))
                                    {
                                        case Utils.Version.VersionComparisonResult.EqualTo:
                                            labelLog.Text = "Status: ATA is up to date!";
                                            break;
                                        case Utils.Version.VersionComparisonResult.LessThan:
                                            labelLog.Text = "Status: ATA is not up to date!";
                                            break;
                                        case Utils.Version.VersionComparisonResult.GreaterThan:
                                            labelLog.Text = "Status: Cool, you are a developer :)";
                                            break;
                                    }
                                }
                                labelLatestRelease.Text = latestRelease;
                                linkLabelChangelog.Visible = true;
                            });
                            return true;
                        });
                    }
                    catch (Exception err)
                    {
                        labelLog.Text = string.Empty;
                        MainForm.MessageShowBox(err.Message + "\nError: " + err.StackTrace, 0);
                    }
                }
                else
                {
                    labelLog.Text = "Status: You are offline";
                }
                runningCheck = false;
            }
            else
            {
                MainForm.MessageShowBox("Wait, check is still running", 2);
            }
        }

        private void buttonCredits_Click(object sender, EventArgs e)
        {
            _ = new About().ShowDialog();
        }

        private void Settings_Load(object sender, EventArgs e)
        {
            labelCurrentRelease.Text = CURRENTVERSION;
            linkLabelChangelog.Visible = false;

            if (!Feedback.checkFeedbackFile())
            {
                checkBoxInitPopUp.Checked = true;
            }
            starting = false;
        }

        private void linkLabelChangelog_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            ScrollableMessageBox.show(changelog, "Changelog");
        }

        private void checkBoxInitPopUp_CheckedChanged(object sender, EventArgs e)
        {
            if (starting)
            {
                return;
            }
            if (checkBoxInitPopUp.Checked)
            {
                _ = Feedback.changeFeedbackFile(false);
                return;
            }
            _ = Feedback.changeFeedbackFile(true);
        }

        private void buttonUpdateLocalSDK_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Yes;
            Close();
        }

        private void buttonDeleteIPHistory_Click(object sender, EventArgs e)
        {
            if (File.Exists(ATA.IPFileName))
            {
                File.Delete(ATA.IPFileName);
            }
        }
    }
}
