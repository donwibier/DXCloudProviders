Usage in you existing application:

Setup the proper connection string in your web.config and specify a container

In the OnInit of the Page or UserControl containing a filemanager, place the following code:

		  protected override void OnInit(EventArgs e)
		  {
				base.OnInit(e);
				
				BlobStorageFileSystemProvider.InitializeFileManager(ASPxFileManager1);
				ASPxFileManager1.CustomFileSystemProvider = new BlobStorageFileSystemProvider("");

				// .... other code ....

		  }
