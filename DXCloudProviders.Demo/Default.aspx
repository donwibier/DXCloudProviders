<%@ Page Language="C#" AutoEventWireup="true" CodeBehind="Default.aspx.cs" Inherits="DXCloudProviders.Demo.Default" %>

<%@ Register Assembly="DevExpress.Web.v14.2, Version=14.2.3.0, Culture=neutral, PublicKeyToken=b88d1754d700e49a" Namespace="DevExpress.Web" TagPrefix="dx" %>

<!DOCTYPE html>

<html xmlns="http://www.w3.org/1999/xhtml">
<head runat="server">
    <title></title>
</head>
<body>
    <form id="form1" runat="server">
    <div>
		  <h1>Using the ASPxFileManager with Dropbox</h1>
		  <div style="float: left">
				<dx:ASPxComboBox id="cbxFileSystem" runat="server" valuetype="System.String" selectedindex="0"
					 caption="Select filesystem">
            <Items>
                <dx:ListEditItem Text="Local folder" Value="Local" />
                <dx:ListEditItem Text="DropBox" Value="Dropbox" />
					 <dx:ListEditItem Text="Azure Blob Storage" Value="Blob" />
            </Items>
            <ClientSideEvents SelectedIndexChanged="function(s, e) { 
						  if(s.GetSelectedIndex() == 0)
								window.location.replace('/Default.aspx');
						  else {
								var url = '/Default.aspx?Service=' + s.GetValue();
								window.location.replace(url);
						  }
                }" />
        </dx:ASPxComboBox>				
		  </div>
		  <div style="float: right">
				<asp:LinkButton runat="server" ID="btLogout" Text="Logout (and loose Dropbox account)" Visible="false" OnClick="btLogout_Click">

				</asp:LinkButton>
		  </div>
		  <br />
		  <dx:ASPxFileManager ID="ASPxFileManager1" runat="server" ClientInstanceName="dxFm">
            <Settings RootFolder="~\" ThumbnailFolder="~\Thumb\" />
            <SettingsFileList>
					 <ThumbnailsViewSettings ThumbnailSize="64px" />
				</SettingsFileList>
            <SettingsEditing AllowCopy="True" AllowCreate="True" AllowDelete="True" AllowMove="True" AllowRename="True" />
            <SettingsFolders EnableCallBacks="True" />
            <SettingsToolbar ShowDownloadButton="True" />
				<SettingsUpload UseAdvancedUploadMode="True">
					 <AdvancedModeSettings EnableMultiSelect="true" />
				</SettingsUpload>
        </dx:ASPxFileManager>
    </div>
    </form>
</body>
</html>
