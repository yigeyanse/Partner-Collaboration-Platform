﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Linq;
using System.Windows.Forms;
using DevExpress.XtraEditors;
using System.IO.Ports;
using PaCSTools;
using System.Data.OracleClient;
using DevExpress.XtraGrid.Columns;

namespace Toner
{
    public partial class GICJ : DevExpress.XtraEditors.XtraForm
    {
        PaCSGlobal global = new PaCSGlobal();
        private SerialPort[] ports = new SerialPort[1];

        private DataTable dt = new DataTable();
        private delegate void InvokeDelegate(string data);
        private string receivedata = "";
        private string docno = "";
        private string currentDocno = "";
        private string lastdocno = "";
        private string operation_window = "";
        private string final_vend_to = "";
        private string final_stock_to = "";
        private bool flagCancel = false;
        private string curentMoveType = "";
        private string curentMoveCode = "";
        private string destMoveType = "";
        private int GIType = 0;//0：buffer 1:其他厂家
        private string destCode = "";
        public GICJ()
        {
            InitializeComponent();
            global.InitMenu();
            ports[0] = new SerialPort();
        }

        private void GICJ_Load(object sender, EventArgs e)
        {
            Init();
        }

        private void Init()
        {
            PaCSGlobal.InitComPort("Toner", "GICJ", ports);

            if (ports[0].IsOpen)
                ports[0].DataReceived += new SerialDataReceivedEventHandler(serialPortGICJ_DataReceived);//重新绑定

            cmbLoc.Properties.BeginUpdate();
            TonerGlobal.LoadStockByVendCode(cmbLoc);
            cmbLoc.Properties.EndUpdate();
            cmbLoc.SelectedIndex = 0;

            cmbDest.Properties.BeginUpdate();
            TonerGlobal.LoadBufferByVendCode(cmbDest);
            cmbDest.Properties.EndUpdate();

            if (PaCSGlobal.LoginUserInfo.Fct_code.Equals("C6H0A"))//2014/12/03
            {
                GIType = 1;//0：buffer 1:其他厂家
                cmbType.SelectedIndex = 1;
                cmbType.Enabled = false;
            }
            
        }

        //使用类型
        private void cmbType_SelectedIndexChanged(object sender, EventArgs e)
        {
            cmbDest.Properties.Items.Clear();
            if (cmbType.SelectedIndex == 0)//到buffer 楼层
            {
                cmbDest.Properties.BeginUpdate();
                TonerGlobal.LoadBufferByVendCode(cmbDest);
                cmbDest.Properties.EndUpdate();
            }
            else if( cmbType.SelectedIndex == 1)//到其他厂家
            {
                cmbDest.Properties.BeginUpdate();
                TonerGlobal.LoadCmbUseVendor(cmbDest, true);
                cmbDest.Properties.EndUpdate();
            }
            else if (cmbType.SelectedIndex == 2)//销售出库(svc,先优)
            {
                cmbDest.Properties.Items.Clear();
            }
        }

        private void serialPortGICJ_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (this.IsHandleCreated)
            {
                System.Threading.Thread.Sleep(100); //读取速度慢，加Sleep延长读取时间, 不可缺少
                //serialPort1.DiscardInBuffer();  //如果不执行上面的代码,serialPort1_DataReceived会执行多次

                int n = ports[0].BytesToRead;
                byte[] buf = new byte[n];
                ports[0].Read(buf, 0, n);
                ports[0].ReceivedBytesThreshold = 31;

                receivedata = System.Text.Encoding.ASCII.GetString(buf);
                receivedata = receivedata.Replace("\r\n", "");

                try
                {
                    this.Invoke(new InvokeDelegate(DoData), receivedata);
                }
                catch (Exception serialPortGICJ_DataReceived)
                {
                    this.Invoke(new EventHandler(delegate
                    {
                        //要委托的代码 
                        lbStatus.Text = "【"+receivedata + "】：出库失败";
                        panelStatus.BackColor = Color.Red;
                        XtraMessageBox.Show(this, "System error[serialPortGICJ_DataReceived]: " + serialPortGICJ_DataReceived.Message);
                    }));
                }
            }
        }

        private void DoData(string data)
        {
            string loc = "";

            flagCancel = checkEdit2.Checked;//是否是出库取消模式

            //要委托的代码 
            tbBucket.Text = receivedata;
            tbBucket.SelectionStart = receivedata.Length;

            DataTable dtThis = TonerGlobal.ScanRecordStatus(data);
            if (dtThis.Rows.Count == 0)
            {
                PaCSGlobal.Speak("库存中不存在");
                lbStatus.Text = "【" + data + "】：库存中不存在";
                panelStatus.BackColor = Color.Yellow;
                return;
            }
            curentMoveType = dtThis.Rows[0]["movetype"].ToString();//获取当前MOVE_TYPE
            curentMoveCode = dtThis.Rows[0]["movecode"].ToString();//获取当前MOVE_CODE

            //add by dyz@2014/10/5
            GIType = cmbType.SelectedIndex;//0：buffer -- 1:其他厂家
            destMoveType = GIType == 0 ? "311" : "351";//目的移动类型
            if (curentMoveType.Equals("999"))
            {
                GI(data);
                return;
            }

            if (flagCancel)
            {
                //出库取消 模式
                currentDocno = dtThis.Rows[0]["currentDocno"].ToString();//获取当前finaldocno
                lastdocno = dtThis.Rows[0]["lastdocno"].ToString();//获取当前lastdocno
                operation_window = dtThis.Rows[0]["operation_window"].ToString();//获取当前operation_window

                if (!operation_window.Equals("GICJ"))
                {
                    PaCSGlobal.Speak("不能在此做取消操作");
                    lbStatus.Text = "【" + data + "】：不能在此做取消操作";
                    panelStatus.BackColor = Color.Yellow;
                    return;
                }
                CancelGI(data);
                tbBucket.Text = "";
            }
            else
            {
                if (cmbDest.SelectedIndex == -1 && cmbType.SelectedIndex != 2)
                {
                    PaCSGlobal.Speak("请选择 出库目的地");
                    lbStatus.Text = "请选择 出库目的地";
                    panelStatus.BackColor = Color.Yellow;
                    cmbDest.Focus();
                    return;
                }
                else
                {
                    destCode = (cmbDest.SelectedItem as ComboxData).Value;//目的地
                }

                final_stock_to = dtThis.Rows[0]["final_stock_to"].ToString();//获取当前final_vend_to
                if (cmbLoc.SelectedIndex > -1)
                {
                    loc = (cmbLoc.SelectedItem as ComboxData).Value;
                    if (!loc.Equals(final_stock_to))
                    {
                        PaCSGlobal.Speak("所在库存位置与选择库存位置不符");
                        lbStatus.Text = "所在库存位置与选择库存位置不符";
                        panelStatus.BackColor = Color.Yellow;
                        return;
                    }
                }

                //出库
                final_vend_to = dtThis.Rows[0]["final_vend_to"].ToString();//获取当前final_vend_to
                if (!final_vend_to.Equals(PaCSGlobal.LoginUserInfo.Venderid))
                {
                    PaCSGlobal.Speak("不在此处，不能进行 【出库】");
                    lbStatus.Text = "【" + data + "】：不在此处，不能进行 【出库】";
                    panelStatus.BackColor = Color.Yellow;
                    return;
                }

                //判断状态
                if (!curentMoveType.Equals("352") && !curentMoveType.Equals("311"))
                {
                    string tip = "";
                    if(int.Parse(curentMoveCode.Substring(4,4))<301)
                    {
                        PaCSGlobal.Speak("碳粉尚未入库，不能进行 【出库】");
                        tip = "碳粉尚未入库，不能进行 【出库】";
                    }
                    else if (int.Parse(curentMoveCode.Substring(4, 4)) > 302)////
                    {
                        PaCSGlobal.Speak("碳粉已经出库，不能再次 【出库】");
                        tip = "碳粉已经出库，不能再次 【出库】";
                    }

                    PaCSGlobal.Speak(tip);
                    lbStatus.Text = "【" + data + "】：" + tip;
                    panelStatus.BackColor = Color.Yellow;
                    return;
                }

                GIType = cmbType.SelectedIndex;

                switch(GIType)
                {
                    case 0:
                        destMoveType = "311";//buffer
                        break;
                    case 1:
                        destMoveType = "351";//其他厂家
                        break;
                }

              //  destMoveType = GIType == 0 ? "311" : "351";//目的移动类型

                 GI(data);
                 tbBucket.Text = "";
            }
        }

        /// <summary>
        /// 厂家 出库
        /// </summary>
        private void GI(string data)
        {
            string moveCode = "";
            switch (GIType)
            {
                case 0:
                    moveCode = "MOVE0401";//buffer
                    break;
                case 1:
                    moveCode = "MOVE0402";//其他厂家
                    break;
            }

            tbDocno.Text = GetDocNo();

            string stock_block = GIType == 0 ? "" : "final_stock_to = null,";//final_stock_to = decode(GIType,0,final_stock_to,null)
            //出库
            string sql = "update pacsd_pm_box set final_move_type = '" + destMoveType + "',final_move_code = '" + moveCode + "',final_vend_to = :final_vend_to, final_vend_from = final_vend_to," +stock_block+
                " final_buffer_to = :final_buffer_to,final_doc_no = :final_doc_no,last_doc_no = final_doc_no,operation_window = 'GICJ',box_case_status = :box_case_status,box_status = :box_status, " +
                " update_date = to_char(sysdate,'yyyyMMdd'),update_time = to_char(sysdate,'hh24miss'),update_user = :update_user,update_ip = :update_ip"+
                " where box_label = '" + data + "' " +
                " and fct_code = '" + PaCSGlobal.LoginUserInfo.Fct_code + "' ";

            OracleParameter[] cmdParam = new OracleParameter[] {
                    new OracleParameter(":final_vend_to", OracleType.VarChar, 50),
                    new OracleParameter(":final_doc_no", OracleType.VarChar, 50),
                    new OracleParameter(":update_user", OracleType.VarChar, 50), 
                    new OracleParameter(":update_ip", OracleType.VarChar, 50),
                    new OracleParameter(":box_case_status", OracleType.VarChar, 50),
                    new OracleParameter(":box_status", OracleType.VarChar, 50),
                    new OracleParameter(":final_buffer_to", OracleType.VarChar, 50)
                    };

            cmdParam[0].Value = GIType == 0 ? PaCSGlobal.LoginUserInfo.Venderid : destCode;
            cmdParam[1].Value = docno;
            cmdParam[2].Value = PaCSGlobal.LoginUserInfo.Id;
            cmdParam[3].Value = PaCSGlobal.GetClientIp();

            DataTable dtStatus = TonerGlobal.GetCommInfoByCode(moveCode);
            if (dtStatus.Rows.Count > 0)
            {
                cmdParam[4].Value = dtStatus.Rows[0]["BOX_CASE_STATUS"].ToString();
                cmdParam[5].Value = dtStatus.Rows[0]["BOX_STATUS"].ToString();
            }
            else
            {
                cmdParam[4].Value = "";
                cmdParam[5].Value = "";
            }

            cmdParam[6].Value = GIType == 0 ? destCode : "";

            int i = OracleHelper.ExecuteNonQuery(sql, cmdParam);
            //插出prog表
            TonerGlobal.InsertIntoProg(data);
            lbStatus.Text = "【" + data + "】：出库成功";
            panelStatus.BackColor = Color.GreenYellow;
            //提示成功语音
            PaCSGlobal.PlayWavOk();
            //刷新列表
            TonerGlobal.SetGridView(GetData(docno), gridView1, gridControl1);
        }

        /// <summary>
        /// 厂家 出库取消
        /// </summary>
        private void CancelGI(string data)
        {
            TonerGlobal.Cancel(lastdocno, data);

            //插入prog表
            TonerGlobal.UpdateProg(currentDocno, data);
            lbStatus.Text = "【" + data + "】：出库取消成功";
            panelStatus.BackColor = Color.GreenYellow;
            //提示成功语音
            PaCSGlobal.PlayWavOk();
            //刷新列表
            TonerGlobal.SetGridView(GetData(""), gridView1, gridControl1);
        }

        private DataTable GetData(string docno)
        {
            StringBuilder sql = new StringBuilder(" select a.final_doc_no DocNo,a.box_label \"桶标签\",a.item \"材料编号\",(select t.vend_nm_cn from pacsm_md_vend t where t.vend_code = a.make_vend_code and t.fct_code = '" + PaCSGlobal.LoginUserInfo.Fct_code + "') \"生产厂家\", " +
               " a.lot_no LotNo,a.box_no BoxNo,a.qty \"数量/千克\",(select t.vend_nm_cn from pacsm_md_vend t where t.vend_code = a.final_vend_to and t.fct_code = '" + PaCSGlobal.LoginUserInfo.Fct_code + "') \"目的厂家\",(select t.comm_code_nm from pacsc_md_comm_code t where  t.type_code ='PACS_BOX_BUFFER' and t.comm_code = a.final_buffer_to and t.fct_code = '" + PaCSGlobal.LoginUserInfo.Fct_code + "') \"目的楼层\"," +
               " to_char(to_date(a.update_date,'yyyymmdd'),'yyyy-mm-dd') \"出库日期\",to_char(to_date(a.update_time,'hh24miss'),'hh24:mi:ss') \"出库时间\",(select u.fullname  from pacs_user u  where u.id = a.update_user) \"出库人\",a.update_ip \"出库IP\" " +
               " from pacsd_pm_box a " +
               " where  operation_window = 'GICJ' "+
               " and (a.final_vend_to = '" + PaCSGlobal.LoginUserInfo.Venderid + "') " +
               " or (a.final_vend_from = '" + PaCSGlobal.LoginUserInfo.Venderid + "') " +
               " and a.update_date between to_char(sysdate-1,'yyyyMMdd') " +
               " and to_char(sysdate,'yyyyMMdd') " +
               " and fct_code = '" + PaCSGlobal.LoginUserInfo.Fct_code + "' ");

            if (!string.IsNullOrEmpty(docno))
            {
                sql.Append(" and a.final_doc_no like '%" + docno + "%'");
            }
            sql.Append(" order by a.final_doc_no desc,a.update_date||a.update_time desc nulls last");
            
            DataTable dtResult = OracleHelper.ExecuteDataTable(sql.ToString());

            if (dtResult.Rows.Count == 0)
            {
                return null;
            }
            return dtResult;
        }

        /// <summary>
        /// 生成DOCNO号
        /// </summary>
        /// <returns></returns>
        private string GetDocNo()
        {
            if (string.IsNullOrEmpty(tbDocno.Text))
            {
                docno = TonerGlobal.GenerateDocNo();
            }
            else
            {
                //if("追加扫描的docno中，有出库的碳粉，不能追加扫描")
                docno = tbDocno.Text.Trim();
            }
            return docno;
        }

        private void btnApply_Click(object sender, EventArgs e)
        {
            try
            {
                docno = tbDocno.Text.Trim();
                TonerGlobal.SetGridView(GetData(docno), gridView1, gridControl1);
            }
            catch (Exception btnApply_Click)
            {
                 XtraMessageBox.Show(this, "System error[btnApply_Click]: " + btnApply_Click.Message);
            }
        }

        private void gridView1_CustomDrawRowIndicator(object sender, DevExpress.XtraGrid.Views.Grid.RowIndicatorCustomDrawEventArgs e)
        {
            if (e.Info.IsRowIndicator && e.RowHandle >= 0)
            {
                e.Info.DisplayText = (e.RowHandle + 1).ToString();
            }
        }

        private void btnExport_Click(object sender, EventArgs e)
        {
            PaCSGlobal.ExportGridToFile(gridView1, "Toner_GICJ");
        }

        private void btnNew_Click(object sender, EventArgs e)
        {
            tbDocno.Text = "";
        }

        private void GIEJH_FormClosing(object sender, FormClosingEventArgs e)
        {
            ports[0].DataReceived -= new SerialDataReceivedEventHandler(serialPortGICJ_DataReceived);//取消绑定

            foreach (SerialPort port in ports)
            {
                if (port.IsOpen)
                {
                    port.Close();
                }
            }
        }

        private void checkEdit2_CheckedChanged(object sender, EventArgs e)
        {
            if (checkEdit2.Checked)
            {
                DialogResult dr = XtraMessageBox.Show("您确认切换到【出库取消】工作模式 吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (dr == DialogResult.OK)
                {
                    checkEdit2.BackColor = Color.Yellow;
                    PaCSGlobal.Speak("现在是【出库取消】工作模式");
                    lbStatus.Text = "现在是【出库取消】工作模式";
                    panelStatus.BackColor = Color.Yellow;
                    tbDocno.Text = "";
                }
                else
                    return;
            }
            else
            {
                DialogResult dr = XtraMessageBox.Show("您确认切换到【正常出库】工作模式 吗?", "提示", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
                if (dr == DialogResult.OK)
                {
                    checkEdit2.BackColor = Color.Transparent;
                    PaCSGlobal.Speak("现在是【正常出库】工作模式");
                    lbStatus.Text = "现在是【正常出库】工作模式";
                    panelStatus.BackColor = Color.Yellow;
                    tbDocno.Text = "";
                }
                else
                    return;
            }
        }

        private void tbDocno_ButtonClick(object sender, DevExpress.XtraEditors.Controls.ButtonPressedEventArgs e)
        {
            try
            {
                AppendScan frmNew = new AppendScan(this.Name);
                DialogResult dg = frmNew.ShowDialog();
                if (dg == DialogResult.OK)
                {
                    tbDocno.Text = frmNew.ReturnValue["DOCNO"];
                }
            }
            catch (Exception tbDocno_ButtonClick)
            {
                lbStatus.Text = "DOCNO获取失败：" + tbDocno_ButtonClick.Message;
                panelStatus.BackColor = Color.Red;
            }
        }

        private void btnCom_Click(object sender, EventArgs e)
        {
            SettingForm setcom = new SettingForm("Toner", "GICJ", 1);
            DialogResult dg = setcom.ShowDialog();

            if (dg == DialogResult.OK)
            {
                PaCSGlobal.InitComPort("Toner", "GICJ", ports);

                if (ports[0].IsOpen)
                    ports[0].DataReceived += new SerialDataReceivedEventHandler(serialPortGICJ_DataReceived);//重新绑定
            }
        }

        private void gridView1_MouseUp(object sender, MouseEventArgs e)
        {
            DevExpress.XtraGrid.Views.Grid.ViewInfo.GridHitInfo hi = this.gridView1.CalcHitInfo(e.Location);
            if (hi.InRow && e.Button == MouseButtons.Right)
            {
                global.CallMenu(gridView1).ShowPopup(Control.MousePosition);
            } 
        }

        private void tbBucket_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)//ENTER键，回车键：确定操作
            {
                if (tbBucket.Text.Trim().Length < 31)
                {
                    XtraMessageBox.Show("非法桶标签！", "提示");
                    return;
                }
                DoData(tbBucket.Text.Trim());
            }
        }

        

   


    }
}