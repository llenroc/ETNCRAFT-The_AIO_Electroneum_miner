﻿using System.Collections.Generic;
using System;
using System.Windows.Forms;
using System.Diagnostics;
using System.IO;
using OpenHardwareMonitor.Hardware;
using System.Net;
using Newtonsoft.Json;
using ETNCRAFT;

namespace ETN_CPU_GPU_MINER
{
    public partial class Form1 : Form
    {
        #region Global vars
        public static string m_Version;
        public static string m_sAggHashData = "";
        public static string m_MiningURL = "";
        public static string m_PoolWebsiteURL = "";
        public static int m_IPoolID = 0;
        public static string m_sETNCRAFTCPULogFileLocation = Application.StartupPath + "\\app_assets\\ETN_CRAFT_CPU_LOG.txt";

        public bool b_FormLoaded = false;
        public bool m_bStartTime = false;
        public bool m_bDebugging = false;
        public bool m_bReadETNCRAFTULog = false;
        public bool m_bTempWarningModalIsOpen = false;
        public bool m_bDoLog = true;

        private Stopwatch stopwatch = new Stopwatch();
        private Logger logger;
        private Logger loggerPool;
        private Messager messager = new Messager();
        private RegistryManager registryManager = new RegistryManager();
        public int m_iTemperatureAlert = 90;

        #endregion

        #region Form Initialization
        private void Form1_Load(object sender, EventArgs e)
        {
            m_cTimer.Interval = 1000;
            m_cTimer.Enabled = true;
            m_cTimer.Start();
        }

        public Form1()
        {
            m_bDoLog = Program.m_bDoLog;
            ProcessUtil.CheckForExistingProcesses();

            if (m_bDoLog)
            {
                logger = new Logger("ETN_Craft");
                loggerPool = new Logger("ETN_Craft_Pool");
                messager.InitializeMessager(logger);
            }


            m_Version = registryManager.GetVersion();
            InitializeComponent();
            //Set version in window header
            this.Text = "ETNCRAFT (" + m_Version + ")";
            this.Update();
            LoadPoolListFromWebsite();
            //force first item in index -- just in case for new user.
            cpuorgpu.SelectedIndex = 0;
            // Check Registry for AutoLoad
            PushStatusMessage("Checking for ETNCRAFT registry keys", m_bDoLog);
            if (registryManager.GetAutoLoad())
                LoadRegistryConfig();
            PushStatusMessage("AutoLoad registry key loaded (" + registryManager.GetAutoLoad() + ")", m_bDoLog);

            // Check Registry for NewMiner
            if (registryManager.GetNewMiner())
            {
                PushStatusMessage("Welcome New Miner!", m_bDoLog);
                DialogResult UserInput = MessageBox.Show("Welcome new miner!\r\nThe help tab has been pre selected.\r\nPlease read and follow the directions.", "WELCOME!", MessageBoxButtons.OK);
                //Load Help tab
                tabs.SelectedTab = tbHelp;
            }

            // cpuorgpu.SelectedItem = cpuorgpu.Items[0];
            //Spool up timers
            InitTemps();
            //This is to keep the event handlers from firing when the form load. Just wrap functions in this.
            b_FormLoaded = true;
            this.FormClosing += new FormClosingEventHandler(CloseForm);
        }

        private void CloseForm(object sender, FormClosingEventArgs e)
        {
            if (m_bDoLog)
                logger.Warn("ETNCRAFT window closed, beginning process cleanup.");
            ProcessUtil.EndProcesses();
            registryManager.CloseRegistryKeys();
        }

        private void notifyIcon1_MouseDoubleClick_1(object sender, MouseEventArgs e)
        {
            Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon1.Visible = false;
        }

        private void Form1_Resize_1(object sender, EventArgs e)
        {
            //if the form is minimized  
            //hide it from the task bar  
            //and show the system tray icon (represented by the NotifyIcon control)  
            if (this.WindowState == FormWindowState.Minimized)
            {
                Hide();
                notifyIcon1.Visible = true;
                notifyIcon1.ShowBalloonTip(2000);
            }
        }
        #endregion

        #region Control Handlers

        #region Click Handlers

        private void BtnStartMining_Click(object sender, EventArgs e)
        {
            //Init Registry Keys just in case dude man decided to delete them.
            registryManager.Initialize();
            SaveConfig();
            registryManager.SetNewMiner(false);
            m_bDebugging = chkDebug.Checked;
            if (!IsWalletValid())
                return;
            SpawnMiner(cpuorgpu.SelectedItem.ToString());
            PushStatusMessage("config.txt updated", m_bDoLog);
            //Start header Timer for app run time
            m_bStartTime = true;
            stopwatch.Start();
            //Start Hash textbox reset timer
            HashTimer();
        }

        private void SpawnMiner(string sComponent)
        {
            #region UPDATE CONFIG
            #region House Keeping
            string sConfig_Template_File_Name = "config_templates/config-etncraft.txt";
            string sConfig_File_Name = "app_assets/config.txt";
            #endregion
            #region Delete and recreate config.txt
            if (File.Exists(sConfig_File_Name))
                File.Delete(sConfig_File_Name);
            else
                File.Create(sConfig_File_Name).Dispose();
            #endregion
            #region push msg
            PushStatusMessage("config created for " + sComponent, m_bDoLog);
            #endregion
            #region  copy template to new config.txt
            File.Copy(sConfig_Template_File_Name, sConfig_File_Name, true);
            #endregion
            #region  replace vars in new config.txt with GUI info
            var CONFIG_CONTENTS = File.ReadAllText(sConfig_File_Name);
            CONFIG_CONTENTS = CONFIG_CONTENTS.Replace("address_replace", m_MiningURL + ":" + port.Text);
            CONFIG_CONTENTS = CONFIG_CONTENTS.Replace("wallet_replace", wallet_address.Text.Replace(" ", ""));
            File.SetAttributes(Application.StartupPath + "\\" + sConfig_File_Name, FileAttributes.Normal);
            File.WriteAllText(Application.StartupPath + "\\" + sConfig_File_Name, CONFIG_CONTENTS);
            #endregion
            #endregion
            #region Spawn miner
            PushStatusMessage("Spawning ETNCRAFT miner for " + sComponent, m_bDoLog);
            string sArgs = "";
            if (sComponent.Equals("CPU"))
                sArgs = "--noAMD --noNVIDIA";
            else if (sComponent.Equals("GPU"))
                sArgs = "--noCPU";

            Process process = ProcessUtil.SpawnMinerProcess(sArgs, m_bDebugging);
            process.OutputDataReceived += (object SenderOut, DataReceivedEventArgs eOut) => PushWorkStatusMessage(eOut.Data);
            process.BeginOutputReadLine();
            process.ErrorDataReceived += (object SenderErr, DataReceivedEventArgs eErr) => PushWorkStatusMessage(eErr.Data);
            process.BeginErrorReadLine();

            #endregion
            StartMining.Enabled = false;
            //BtnStopMining.Enabled = true;
        }

        private void BtnStopMining_Click(object sender, EventArgs e)
        {
            //Stop Timer
            m_bStartTime = false;
            stopwatch.Stop();
            //Kill mining
            ProcessUtil.EndProcesses();
            StartMining.Enabled = true;
        }

        private void BtnCheckBalance_Click(object sender, EventArgs e)
        {
            string webAddress = m_PoolWebsiteURL;
            Process.Start(webAddress);
        }

        private void BtnClearWallet_Click(object sender, EventArgs e)
        {
            wallet_address.Text = "";
            status.Text = messager.ClearMessages();
        }

        private void BtnOpenLog_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo()
            {
                UseShellExecute = true,
                FileName = logger.GetLogFilePath()
            });
            Process.Start(new ProcessStartInfo()
            {
                UseShellExecute = true,
                FileName = loggerPool.GetLogFilePath()
            });
        }

        private void ClearMessagesButton_Click(object sender, EventArgs e)
        {
            status.Text = messager.ClearMessages();
            WorkStatus.Text = "";
            m_sAggHashData = "";
        }

        private void BtnLoadConfig_Click(object sender, EventArgs e)
        {
            registryManager.Initialize();
        }

        private void BtnSaveConfig_Click(object sender, EventArgs e)
        {
            SaveConfig();
        }

        private void BtnLoadDefaultConfig_Click(object sender, EventArgs e)
        {
            registryManager.DeleteRegistryKey();
            registryManager.Initialize();
            Application.Restart();
            Environment.Exit(0);
        }

        private void LinkWalletGen_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://my.electroneum.com/offline_paper_electroneum_walletV1.6.html");
        }

        private void btnETNWorth_Click(object sender, EventArgs e)
        {
            MessageBox.Show(GetCurrentCoinPrice(), "Current ETN Worth");
        }

        private void btnDeleteRegKeys_Click(object sender, EventArgs e)
        {
            MessageBox.Show(registryManager.DeleteRegistryKey(), "ETNCRAFT Services");
        }
        #endregion

        #region DropDown Handlers

        private void pool_SelectedIndexChanged_1(object sender, EventArgs e)
        {
            PoolsCollection cPoolCollection = new PoolsCollection();
            cPoolCollection.Load();
            foreach (var cItem in cPoolCollection)
            {
                if (cItem.sDisplayName.Equals(cboPool.SelectedItem.ToString()))
                {
                    m_MiningURL = cItem.sPoolMiningURL;
                    m_PoolWebsiteURL = cItem.sPoolWebsite;
                    m_IPoolID = cItem.iID;
                    PushStatusMessage(cItem.sDisplayName + " selected, " + cItem.sPoolWebsite + " | " + cItem.sPoolInformation, m_bDoLog);
                    break;
                }
            }
        }

        private void txtCustomPool_TextChanged(object sender, EventArgs e)
        {
            if (!txtCustomPool.Equals(""))
            {
                m_MiningURL = txtCustomPool.Text;
                PushStatusMessage("custom pool selected[" + txtCustomPool.Text + "], make sure to add your pool address!", m_bDoLog);
            }
        }

        #endregion

        #region Check Box Handlers

        private void chkAutoLoadConfig_CheckedChanged(object sender, EventArgs e)
        {
            if (b_FormLoaded)
            {
                DialogResult UserInput;
                if (chkAutoLoadConfig.Checked)
                {
                    new DialogResult();
                    UserInput = MessageBox.Show("Make sure your wallet information has been entered!\r\nThis will save your info and also setup auto config when the app restarts\r\nClick yes to continue", "ALERT", MessageBoxButtons.YesNo);
                    if (UserInput == DialogResult.No)
                    {
                        registryManager.SetAutoLoad(false);
                        chkAutoLoadConfig.Checked = false;
                    }
                    else
                    {
                        SaveConfig();
                        registryManager.SetAutoLoad(chkAutoLoadConfig.Checked);

                    }
                }
                else
                    registryManager.SetAutoLoad(false);
            }
            PushStatusMessage("AutoLoad registry key updated.", m_bDoLog);
        }

        #endregion


        #endregion

        #region Utility Methods

        #region Config/Registry

        private void LoadRegistryConfig()
        {
            PushStatusMessage("Loading ETNCRAFT config from registry", m_bDoLog);
            wallet_address.Text = registryManager.GetWalletId();
            port.Text = registryManager.GetPortNumber();
            chkAutoLoadConfig.Checked = registryManager.GetAutoLoad();
            txtCustomPool.Text = registryManager.GetCustomPool();
            cpuorgpu.SelectedItem = registryManager.GetComponent();
            txtTempLimit.Text = CheckTempLimitEntry(registryManager.GetTempLimit());
            m_iTemperatureAlert = int.Parse(txtTempLimit.Text);
            #region Get Pool and select drop down
            bool bFoundPool = false;
            //i know i know.... this is the wrong way to go about this. Just for quick testing of registry additions. Git blame Liam
            PoolsCollection cPools = new PoolsCollection();
            cPools.Load();
            if (cPools.Count > 0)
                foreach (var Pool in cPools)
                    if (Pool.iID.Equals(registryManager.GetPool()))
                    {
                        int index = cboPool.FindString(Pool.sDisplayName);
                        cboPool.SelectedIndex = index;
                        m_IPoolID = Pool.iID;
                        bFoundPool = true;
                        break;
                    }
            if (!bFoundPool)
                PushStatusMessage("Saved pool no longer exists in our database", m_bDoLog);
            #endregion
        }

        private void SaveConfig()
        {
            registryManager.SetAutoLoad(chkAutoLoadConfig.Checked);
            registryManager.SetCustomPool(txtCustomPool.Text);
            registryManager.SetComponent(cpuorgpu.SelectedItem.ToString());
            registryManager.SetPort(port.Text);
            registryManager.SetPool(m_IPoolID);
            registryManager.SetWalletId(wallet_address.Text);
            registryManager.SetTempLimit(CheckTempLimitEntry(txtTempLimit.Text));
            PushStatusMessage("Configuration Updated", m_bDoLog);
        }
        private void txtTempLimit_TextChanged(object sender, EventArgs e)
        {
            //registryManager.SetTempLimit(CheckTempLimitEntry(txtTempLimit.Text));
            m_iTemperatureAlert = int.Parse(CheckTempLimitEntry(txtTempLimit.Text));
        }

        #endregion

        #region Timers/Temperature Data/CPU LOG READ
        #region Hash Timers & Miner log parse
        private void HashTimer()
        {
            Timer HashTxtTimer = new Timer();
            HashTxtTimer.Interval = 600000;//10 minutes
            HashTxtTimer.Tick += new System.EventHandler(Hashtxt_Tick);
            HashTxtTimer.Start();

        }
        private void Hashtxt_Tick(object sender, EventArgs e)
        {
            WorkStatus.Text = "Log Cleared!";
            m_sAggHashData = "";
            // PushStatusMessage(GetCurrentCoinPrice());
        }
        #endregion

        #region Temp/Uptime Timers etc
        void timer_Tick(object sender, EventArgs e)
        {
            double m_dElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            if (m_dElapsedSeconds < Program.m_iMaxRuntime || Program.m_iMaxRuntime == 0)
            {
                GetSysTemp();
                #region Timer in window header
                if (m_bStartTime)
                {
                    this.Text = "ETNCRAFT " + m_Version + " | Uptime " + String.Format("{0}:{1}:{2}", stopwatch.Elapsed.Hours.ToString("00"), stopwatch.Elapsed.Minutes.ToString("00"), stopwatch.Elapsed.Seconds.ToString("00")); ;
                    this.Update();
                }
                #endregion
            }            
            else
            {                
                PushStatusMessage("Maximum UpTime Limit Reached At : " + String.Format("{0}:{1}:{2}", stopwatch.Elapsed.Hours.ToString("00"), stopwatch.Elapsed.Minutes.ToString("00"), stopwatch.Elapsed.Seconds.ToString("00")));
                stopwatch.Stop();
                PushStatusMessage("Cleanup in preperation for shutdown");                    
                ProcessUtil.EndProcesses();
                PushStatusMessage("Done. Shutting down.");
                Environment.Exit(0);                               
            }
        }
        Computer myComputer;
        Timer timer = new Timer { Enabled = true, Interval = 1000 };
        public void InitTemps()
        {

            timer.Tick += new EventHandler(timer_Tick);

            GetTemperature.System settings = new GetTemperature.System(new Dictionary<string, string>
            {
                { "/intelcpu/0/temperature/0/values", "H4sIAAAAAAAEAOy9B2AcSZYlJi9tynt/SvVK1+B0oQiAYBMk2JBAEOzBiM3mkuwdaUcjKasqgcplVmVdZhZAzO2dvPfee++999577733ujudTif33/8/XGZkAWz2zkrayZ4hgKrIHz9+fB8/Iu6//MH37x79i9/+NX6N3/TJm9/5f/01fw1+fosnv+A/+OlfS37/jZ/s/Lpv9fff6Ml/NTef/yZPnozc5679b+i193//TQZ+/w2Dd+P9/sZeX/67v/GTf/b3iP3u4/ObBL//73+i+f039+D8Zk/+xz/e/P6beu2TQZju8yH8f6OgzcvPv/U3/Rb8+z/0f/9b/+yfaOn8079X6fr6Cws7ln/iHzNwflPv99/wyS/+xY4+v/evcJ+733+jJ5//Cw7/4ndy9Im3+U2e/Fbnrk31C93vrt/fyPvdb+N//hsF7/4/AQAA//9NLZZ8WAIAAA==" },
                { "/intelcpu/0/load/0/values", "H4sIAAAAAAAEAOy9B2AcSZYlJi9tynt/SvVK1+B0oQiAYBMk2JBAEOzBiM3mkuwdaUcjKasqgcplVmVdZhZAzO2dvPfee++999577733ujudTif33/8/XGZkAWz2zkrayZ4hgKrIHz9+fB8/Iu6//MH37x79i9++mpwcv/md/9df89egZ/xX/ym/5y/4D37618Lv7ya//u+58+u+5d9/z7/5t/w9/6u5fP5bH/6av+eTkXyefXxp26ONaf/v/dG/sf39D/rvnv4e5vc/0IP56/waK/vuHzf5I38P8/tv+mv8Rbb9f0pwTF9/zr/1X9vP/8I//+/6Pf7Z30N+/zdf/HX29zd/859q4aCNP5b//U+U3/+7f+zXOjZwfqvDX/V7/o9/vPz+a1G/pv0f+fGlhfk7eZ//N3/0v28//5X0u/n8Cxq7+f1X/tHft20A5x8a/W5/02+BP36Nf+j/nv8XfzrT+c2//Ob4p3+vktvUhNs/+xcWikP6e/4T/5jS5M8/sL8vP/5ff49f/Ivl9//sHzv6PX/vXyG//9R/94/9HuZ34P/5vyC//3W/5e/1exa/k+Bw4bUBnU2bP4Xg/1bn0uafeTH6PatfKL//N3/0t2y/gG9+/8+IzqYNxmU+/+jwX7afY67/nwAAAP//GYSA31gCAAA=" },
            });

            myComputer = new Computer(settings)
            {
                GPUEnabled = true,
                CPUEnabled = true
                //MainboardEnabled = true,
                //RAMEnabled = true,
                // FanControllerEnabled = true,
                //HDDEnabled = true
            };
            myComputer.Open();
        }
        public void GetSysTemp()
        {
            lblCPUTemp.Text = "";
            lblCPUUsage.Text = "";
            lblGPUTemp.Text = "";
            lblGPUUsage.Text = "";

            foreach (var hardwareItem in myComputer.Hardware)
            {
                hardwareItem.Update();
                if (hardwareItem.HardwareType.Equals(HardwareType.CPU))
                {
                    hardwareItem.Update();
                    foreach (IHardware subHardware in hardwareItem.SubHardware)
                        subHardware.Update();

                    foreach (var sensor in hardwareItem.Sensors)
                    {
                        if (sensor.SensorType.Equals(SensorType.Temperature))
                        {
                            lblCPUTemp.Text += (String.Format("{0} = {1}C", sensor.Name, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value") + "\r\n");
                            if (sensor.Value > m_iTemperatureAlert)
                                HighTempAlert(sensor.Name);
                        }
                        else if (sensor.SensorType.Equals(SensorType.Load))
                            lblCPUUsage.Text += (String.Format("{0} = {1}%", sensor.Name, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value") + "\r\n");

                    }
                }
                if (hardwareItem.HardwareType.Equals(HardwareType.GpuAti) || hardwareItem.HardwareType.Equals(HardwareType.GpuNvidia))
                {
                    foreach (IHardware subHardware in hardwareItem.SubHardware)
                        subHardware.Update();

                    foreach (var sensor in hardwareItem.Sensors)
                    {
                        if (sensor.SensorType.Equals(SensorType.Temperature))
                        {
                            lblGPUTemp.Text += (String.Format("{0} = {1}C", sensor.Name, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value") + "\r\n");
                            if (sensor.Value > m_iTemperatureAlert)
                                HighTempAlert(sensor.Name);
                        }
                        else if (sensor.SensorType.Equals(SensorType.Load))
                            lblGPUUsage.Text += (String.Format("{0} = {1}%", sensor.Name, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value") + "\r\n");
                    }
                }
            }
            #region Use This loop for all hardware info
            //foreach (var hardwareItem in myComputer.Hardware)
            //{
            //    hardwareItem.Update();

            //    if (hardwareItem.SubHardware.Length > 0)
            //    {
            //        foreach (IHardware subHardware in hardwareItem.SubHardware)
            //        {
            //            subHardware.Update();

            //            foreach (var sensor in subHardware.Sensors)
            //            {

            //                lblGPUTemp.Text += (String.Format("{0} {1} = {2}", sensor.Name, sensor.Hardware, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value"));
            //            }
            //        }
            //    }
            //    else
            //    {
            //        foreach (var sensor in hardwareItem.Sensors)
            //        {

            //            lblGPUTemp.Text += (String.Format("{0} {1} = {2}", sensor.Identifier, sensor.Hardware, sensor.Value.HasValue ? sensor.Value.Value.ToString() : "no value"));
            //        }
            //    }
            //}
        }
        #endregion

        private void HighTempAlert(string sDevice)
        {
            if (!m_bTempWarningModalIsOpen && !registryManager.GetIgnoreTempWarnings())
            {
                m_bTempWarningModalIsOpen = true;
                DialogResult UserInput = MessageBox.Show(sDevice + " Temps are above " + m_iTemperatureAlert.ToString() + " degrees!\r\nConsider turning fan speeds higher.\r\n\r\nSave ignore warnings in config?", "WARNING!", MessageBoxButtons.YesNo);
                if (UserInput.Equals(DialogResult.Yes))
                    registryManager.SetIgnoreTempWarnings(true);
                else
                    registryManager.SetIgnoreTempWarnings(false);
            }
        }
        #endregion

        #region Logger/Messager

        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        private void PushStatusMessage(string message, bool doLog = true)
        {
            if (message != null)
            {
                status.Text = messager.PushMessage(message, doLog);
                status.SelectionStart = status.Text.Length;
                status.ScrollToCaret();
            }
        }

        private void PushWorkStatusMessage(string message)
        {
            if (message != null && !message.Equals(""))
            {
                string cleanMessage = RemoveAnsiEscapes(message);
                m_sAggHashData += cleanMessage + "\r\n";
                ThreadHelperClass.SetText(this, WorkStatus, m_sAggHashData);
                if (m_bDoLog)
                    loggerPool.Debug(cleanMessage);

                try
                {
                    //TEMP SOLUTION -- COMMENT THESE TWO LINES OUT IN LOCAL DEBUG
                    WorkStatus.SelectionStart = WorkStatus.Text.Length;
                    WorkStatus.ScrollToCaret();
                }
                catch { }
            }
        }

        #endregion

        private string RemoveAnsiEscapes(string message)
        {
            //string cleanMessage = Regex.Replace(message, @"\\u(?<Value>[a-zA-Z0-9]{4})", "");
            return message;
        }

        private bool IsWalletValid()
        {
            if (wallet_address.Text.Equals("Enter Public Wallet Here") || wallet_address.Text.Equals("") || wallet_address.Text.Equals("EnterPublicWalletHere"))
            {
                DialogResult UserInput = MessageBox.Show("Developer Wallet Will Be Used!\r\nARE YOU SURE?!", "READ THIS", MessageBoxButtons.OKCancel);
                if (UserInput.Equals(DialogResult.Cancel))
                {
                    return false;
                }
                else if (UserInput.Equals(DialogResult.OK))
                {
                    wallet_address.Text = "etnk73mQE5yfqZUnMYeJPyJUb5AigTtox8cgd3zw493uRwgG6fKXUdeaBcny4kuy5DN3XiizKUCPjM2ySkJvK9Cm7ZTGJMr7gT";
                    PushStatusMessage("Developer Wallet Address Selected! Thanks!", m_bDoLog);
                    return true;
                }
            }
            return true;
        }
       
        private void LoadPoolListFromWebsite()
        {
            //Set Path
            string filepath = Application.StartupPath + "\\app_assets\\pools.txt";
            //Download doc from website
            WebClient webClient = new WebClient();
            webClient.DownloadFile("http://liamthrower.com/pools.txt", filepath);
            //Build Collection of pools
            PoolsCollection cAllPools = new PoolsCollection();
            cAllPools.Load();
            foreach (var citem in cAllPools)
                cboPool.Items.Add(new PoolComboBoxItems(citem.iID.ToString(), citem.sDisplayName));
            //Force selected item in dropdown list and fire the onselected change event which sets some global vars
            cboPool.SelectedIndex = 0;

        }

        private void m_cTimer_Tick(object sender, EventArgs e)
        {
            m_cTimer.Stop();
            m_cTimer.Enabled = false;

            if (Program.m_bAutoRun)
                BtnStartMining_Click(sender, e);                

            if (Program.m_bMinimize)
                WindowState = FormWindowState.Minimized;
        }

        #endregion


        private string CheckTempLimitEntry(string sText)
        {
            bool bFailed = false;
            string sTemperature = "";
            if (!string.IsNullOrWhiteSpace(sText))
            {
                int temp;
                if (int.TryParse(sText, out temp))
                    if (temp >= 0)
                        sTemperature = sText;
                    else
                        bFailed = true;
                else
                    bFailed = true;
            }
            else
                sTemperature = "90";
            if (bFailed)
            {
                sTemperature = "90";
                PushStatusMessage("Temp field in not an int. Limit set to 90", m_bDoLog);
            }
            return sTemperature;
        }

        private string GetCurrentCoinPrice()
        {
            string sETNUSD = "";
            var json = new WebClient().DownloadString("https://api.nanopool.org/v1/etn/prices");
            if (!json.Equals(""))
            {
                PRICE_Rootobject r = JsonConvert.DeserializeObject<PRICE_Rootobject>(json);
                sETNUSD =  "ETN Price USD: " + r.data.price_usd + "\r\n";
                sETNUSD += "ETN Price BTN: " + r.data.price_btc + "\r\n";
                sETNUSD += "ETN Price EUR: " + r.data.price_eur + "\r\n";
                sETNUSD += "ETN Price RUR: " + r.data.price_rur + "\r\n";
                sETNUSD += "ETN Price CNY: " + r.data.price_cny + "\r\n";
            }
            return sETNUSD;
        }

        #endregion

        private void wallet_address_Click(object sender, EventArgs e)
        {
            wallet_address.SelectAll();
        }
    }
    public class PRICE_Rootobject
    {
        public bool status { get; set; }
        public PRICE_Data data { get; set; }
    }

    public class PRICE_Data
    {
        public float price_btc { get; set; }
        public float price_usd { get; set; }
        public float price_eur { get; set; }
        public float price_rur { get; set; }
        public float price_cny { get; set; }
    }

}
