using DevExpress.Web;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Web;
using System.Web.Hosting;
using System.Web.UI.WebControls;
using System.Threading.Tasks;

namespace DXCloudProviders.Services.Azure
{
	 public class BlobStorageFileSystemProvider : FileSystemProviderBase
	 {
		  const string DX_HIDDEN_FILE = ".dx_dummy.txt";
		  const string DX_HIDDEN_FILE_CONTENT = "-- This file is uploaded by the ASPxFileManager by DevExpress to mimic folder creation --";

		  public BlobStorageFileSystemProvider(string rootFolder)
				: base(rootFolder)
		  {
				Trace.WriteLine("BlobStorageFileSystemProvider constructor called");
		  }

		  #region some static helper methods
		  public static readonly string ThumbnailRootFolder = "~/Temp/Thumbs/";
		  static string MakeRelativePath(params string[] parts)
		  {
				return MakeRelativePath(true, parts);
		  }
		  static string MakeRelativePath(bool addLeadingSlash, params string[] parts)
		  {

				List<string> result = new List<string>();
				try
				{
					 foreach (string p in parts)
					 {
						  if (!String.IsNullOrEmpty(p))
								result.AddRange(p.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries));
					 }
				}
				catch
				{
					 Trace.WriteLine("Exception");
				}
				string leadingChar = addLeadingSlash ? "/" : "";
				return String.Format("{0}{1}", (result.Count > 0 && result[0] == "~" ? "" : leadingChar), String.Join("/", result.ToArray())).Replace(" ", "%20");
		  }

		  static bool ParseRelativePath(string path, string folderSeparator, out string folder, out string filename, out string extension, out string parameters)
		  {
				folder = String.Empty;
				filename = String.Empty;
				extension = String.Empty;
				parameters = String.Empty;
				if (String.IsNullOrEmpty(path))
					 return false;
				else
				{
					 // strip querystring parameters
					 string[] parts = path.Split(new char[] { '?' }, 2);
					 if (parts.Length > 1)
						  parameters = parts[1];

					 // get path parts
					 List<string> pathParts = new List<string>(parts[0].Split(new char[] { '/', '\\' }));
					 if (pathParts.Count > 0)
					 {
						  //remove last part from list (is filename+ext)
						  string name = pathParts[pathParts.Count - 1];
						  pathParts.RemoveAt(pathParts.Count - 1);
						  //get extension
						  int dotPos = name.LastIndexOf(".");
						  if (dotPos > 0)
						  {
								extension = name.Substring(dotPos);
								filename = name.Substring(0, dotPos);
						  }
						  else
								filename = name;
						  //compose folder
						  folder = (pathParts.Count > 0 && (pathParts[0] == "~" || pathParts[0].EndsWith(":")) ? "" : folderSeparator) +
								String.Join(folderSeparator, pathParts.ToArray());
					 }
					 else
						  folder = folderSeparator;
					 return true;
				}
		  }

		  private static void GetThumbNails(CloudBlobContainer container, string accountName, BlobFolder blobFolder, string thumbNailRootFolder)
		  {
				foreach (BlobFile file in blobFolder.Files)
				{					 
					 string thumbUrl = GetThumbNailUrl(thumbNailRootFolder, accountName, MakeRelativePath(false, file.FullPath));
					 if (!String.IsNullOrEmpty(thumbUrl))
					 {
						  string thumbPath = HostingEnvironment.MapPath(thumbUrl);
						  if (!File.Exists(thumbPath))
						  {
								string blobRef = MakeRelativePath(false, file.FullPath).Substring(container.Name.Length + 1);

								var blobFile = container.GetBlockBlobReference(blobRef);
								blobFile.FetchAttributes();
								// check if download is needed
								if (blobFile.Properties.LastModified > File.GetLastWriteTimeUtc(thumbPath))
								{
									 using (MemoryStream ms = new MemoryStream())
									 {
										  blobFile.DownloadToStream(ms);

										  System.Drawing.Image img = System.Drawing.Image.FromStream(ms);
										  System.Drawing.Bitmap thumb = CropThumbnail(img, 64);
										  try
										  {
												lock (fileLock)
												{
													 Directory.CreateDirectory(Path.GetDirectoryName(thumbPath));
													 thumb.Save(thumbPath);
												}
										  }
										  finally
										  {
												img.Dispose();
												thumb.Dispose();
										  }
									 }
								}
						  }
					 }
				}
				foreach (var folder in blobFolder.Folders)
				{
					 GetThumbNails(container, accountName, folder, thumbNailRootFolder);
				}
		  }

		  static System.Drawing.Bitmap CropThumbnail(System.Drawing.Image original, int size)
		  {
				System.Drawing.Bitmap thumbnail = new System.Drawing.Bitmap(size, size);
				int newHeight = original.Height;
				int newWidth = original.Width;
				if (original.Height > size || original.Width > size)
				{
					 newHeight = (original.Height > original.Width) ? size : (int)(size * original.Height / original.Width);
					 newWidth = (original.Width > original.Height) ? size : (int)(size * original.Width / original.Height);
				}
				System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(thumbnail);
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
				int top = (int)(size - newHeight) / 2;
				int left = (int)(size - newWidth) / 2;
				g.DrawImage(original, left, top, newWidth, newHeight);
				return thumbnail;
		  }

		  static string GetCacheKey(string accountKey, string container, string relativeFolder)
		  {
				return String.Format("{0}_{1}_{2}", accountKey, container, relativeFolder);
		  }

		  enum fileOperation { create, copy, move, delete, rename };

		  static void FileOperation(CloudBlobClient client, string blobContainer, string source, fileOperation operation, string destination = "")
		  {
				if (client == null)
					 throw new ArgumentNullException("consumer");
				if (String.IsNullOrEmpty(blobContainer))
					 throw new ArgumentNullException("blobContainer");
				if (String.IsNullOrEmpty(source))
					 throw new ArgumentNullException("source");

				CloudBlobContainer container = client.GetContainerReference(blobContainer);
				container.CreateIfNotExists(BlobContainerPublicAccessType.Container);

				var blobSource = container.GetBlobReferenceFromServer(source);
				switch (operation)
				{
					 case fileOperation.copy:
					 case fileOperation.move:
					 case fileOperation.rename:
						  var blobDestination = container.GetBlockBlobReference(destination);
						  blobDestination.StartCopyFromBlob(blobSource.Uri);
						  if (operation != fileOperation.copy)
								blobSource.Delete();
						  break;
					 case fileOperation.delete:
						  blobSource.Delete();
						  break;
					 default:
						  break;
				}
				ClearContainer(blobContainer, client);
		  }

		  static void ProcessFolder(CloudBlobDirectory sourceFolder, string destination, fileOperation operation)
		  {

				string folder = sourceFolder.Uri.AbsolutePath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last();
				switch (operation)
				{
					 case fileOperation.copy:
					 case fileOperation.move:
					 case fileOperation.rename:
						  foreach (var item in sourceFolder.ListBlobs(false))
						  {
								if (item.GetType() == typeof(CloudBlobDirectory))
								{

									 ProcessFolder(item as CloudBlobDirectory, MakeRelativePath(false, destination, folder), operation);
								}
								else /*if (item.GetType() == typeof(CloudBlockBlob))*/
								{
									 string destinationFile = MakeRelativePath(false, destination, (operation == fileOperation.rename ? "" : folder), VirtualPathUtility.GetFileName(item.Uri.AbsolutePath));
									 var blobDestination = sourceFolder.Container.GetBlockBlobReference(destinationFile);
									 blobDestination.StartCopyFromBlob(item.Uri);
									 if (operation != fileOperation.copy)
										  ((CloudBlockBlob)item).Delete();
								}
						  }
						  break;
					 case fileOperation.delete:
						  foreach (var item in sourceFolder.ListBlobs(false))
						  {
								if (item.GetType() == typeof(CloudBlobDirectory))
								{
									 ProcessFolder(item as CloudBlobDirectory, String.Empty, operation);
								}
								else /*if (item.GetType() == typeof(CloudBlockBlob))*/
								{
									 ((CloudBlockBlob)item).Delete();
								}
						  }
						  break;
					 
					 default:
						  break;
				}

		  }
		  static void FolderOperation(CloudBlobClient client, string blobContainer, string source, fileOperation operation, string destination = "")
		  {
				if (client == null)
					 throw new ArgumentNullException("consumer");
				if (String.IsNullOrEmpty(blobContainer))
					 throw new ArgumentNullException("blobContainer");
				if (String.IsNullOrEmpty(source))
					 throw new ArgumentNullException("source");

				CloudBlobContainer container = client.GetContainerReference(blobContainer);
				container.CreateIfNotExists(BlobContainerPublicAccessType.Container);
				if (operation == fileOperation.create)
				{
					 string dummyFile = MakeRelativePath(false, source, DX_HIDDEN_FILE);
					 var blobDummy = container.GetBlockBlobReference(dummyFile);
					 blobDummy.UploadText(DX_HIDDEN_FILE_CONTENT);
				}
				else
				{
					 var blobSource = container.GetDirectoryReference(source);
					 ProcessFolder(blobSource, destination, operation);
				}
				ClearContainer(blobContainer, client);
		  }

		  #endregion
		  static void ClearContainer(string blobContainer, CloudBlobClient client)
		  {
				HostingEnvironment.Cache.Remove(GetCacheKey(client.Credentials.ExportBase64EncodedKey(), blobContainer, "blobCache"));
		  }
		  static BlobFolder GetContainer(string relativeFolder, string blobContainer, CloudBlobClient client)
		  {
				string folderToFind = MakeRelativePath(blobContainer, relativeFolder);
				BlobCache cache = HostingEnvironment.Cache[GetCacheKey(client.Credentials.ExportBase64EncodedKey(), blobContainer, "blobCache")] as BlobCache;				
				BlobFolder result = (cache != null) ? cache.FindFolder(folderToFind) : null;				

				if ((cache == null) || (result == null))
				{
					 Trace.WriteLine(String.Format("GetFiles: Requesting '{0}' from Azure Blob Storage", blobContainer));
					 try
					 {
						  CloudBlobContainer container = client.GetContainerReference(blobContainer);
						  container.CreateIfNotExists(BlobContainerPublicAccessType.Container);

						  cache = new BlobCache("/" + blobContainer);
						  foreach (var file in container.ListBlobs(null, false))
						  {
								cache.AddItem(file, null);
						  }
						  HostingEnvironment.Cache.Add(GetCacheKey(client.Credentials.ExportBase64EncodedKey(), blobContainer, "blobCache"), cache, null,
								System.Web.Caching.Cache.NoAbsoluteExpiration, new TimeSpan(0, 4, 0), System.Web.Caching.CacheItemPriority.Normal, null);

						  result = cache.FindFolder(folderToFind);

						  // fetch and check thumbnails in background task
						  Task.Factory.StartNew(() =>
						  {
						    Trace.WriteLine("Checking thumbNails in Background Task");
							 CloudBlobContainer cnt = GetAccount().CreateCloudBlobClient().GetContainerReference(blobContainer);
							 GetThumbNails(cnt, client.Credentials.AccountName, cache.RootFolder, ThumbnailRootFolder);
						  });
					 }
					 catch (Exception err)
					 {
						  Trace.WriteLine(err.InnerException == null ? err.Message : err.InnerException.Message);
						  result = null;
					 }
				}
				else
				{
					 Trace.WriteLine(String.Format("Getting '{0}' from cache", blobContainer));
				}

				return result;
		  }

		  static CloudStorageAccount GetAccount()
		  {
				string connectionString = ConfigurationManager.AppSettings["DXBlobStorageConnection"] ?? "StorageConnectionString";
				return CloudStorageAccount.Parse(ConfigurationManager.ConnectionStrings[connectionString].ConnectionString);
		  }

		  private CloudStorageAccount _account = null;
		  protected CloudStorageAccount account
		  {
				get
				{
					 if (_account == null)
					 {
						  _account = GetAccount();
					 }
					 return _account;
				}
		  }


		  protected static string BlobContainer
		  {
				get
				{
					 string container = ConfigurationManager.AppSettings["DXBlobContainer"];
					 if (String.IsNullOrEmpty(container))
						  throw new ConfigurationErrorsException("Please specify the Blob Container in the web.config AppSettings[\"DXBlobContainer\"]");
					 return container;
				}
		  }
		  public override string RootFolderDisplayName { get { return String.Format("Azure Storage: {0}", BlobContainer); } }


		  public override IEnumerable<FileManagerFile> GetFiles(FileManagerFolder folder)
		  {
				string relativePath = MakeRelativePath(folder.RelativeName);
				var files = from n in GetContainer(relativePath, BlobContainer, account.CreateCloudBlobClient()).Files
								select new FileManagerFile(this, folder, n.Name);

				return files;

		  }

		  public override IEnumerable<FileManagerFolder> GetFolders(FileManagerFolder parentFolder)
		  {
				string relativePath = MakeRelativePath(parentFolder.RelativeName);
				var folders = from n in GetContainer(relativePath, BlobContainer, account.CreateCloudBlobClient()).Folders
								  select new FileManagerFolder(this, parentFolder, n.Name);
				return folders;
		  }

		  public override Stream ReadFile(FileManagerFile file)
		  {
				return null;
		  }

		  public override bool Exists(FileManagerFile file)
		  {
				string relativeFile = MakeRelativePath(file.RelativeName);
				string relativeFolder = MakeRelativePath(file.Folder.RelativeName);

				var f = (from n in GetContainer(relativeFolder, BlobContainer, account.CreateCloudBlobClient()).Files
							where (n.Name == file.Name)
							select n).FirstOrDefault();

				return f != null;
		  }

		  public override bool Exists(FileManagerFolder folder)
		  {
				string relativePath = MakeRelativePath(folder.RelativeName);
				BlobFolder f = GetContainer(relativePath, BlobContainer, account.CreateCloudBlobClient());
				return f != null;

		  }


		  public override void CopyFile(FileManagerFile file, FileManagerFolder newParentFolder)
		  {
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				string destinationFile = MakeRelativePath(false, newParentFolder.RelativeName, file.Name);


				FileOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFile, fileOperation.copy, destinationFile);
		  }

		  
		  public override void CopyFolder(FileManagerFolder folder, FileManagerFolder newParentFolder)
		  {
				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);
				FolderOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFolder, fileOperation.copy, destinationFolder);
		  }

		  public override void CreateFolder(FileManagerFolder parent, string name)
		  {
				string newFolder = MakeRelativePath(false, parent.RelativeName, name);
				FolderOperation(account.CreateCloudBlobClient(), BlobContainer, newFolder, fileOperation.create);
		  }

		  public override void DeleteFile(FileManagerFile file)
		  {
				string path = MakeRelativePath(false, file.RelativeName);
				FileOperation(account.CreateCloudBlobClient(), BlobContainer, path, fileOperation.delete);
		  }

		  public override void DeleteFolder(FileManagerFolder folder)
		  {
				string path = MakeRelativePath(false, folder.RelativeName);
				FolderOperation(account.CreateCloudBlobClient(), BlobContainer, path, fileOperation.delete);
		  }


		  public override void MoveFile(FileManagerFile file, FileManagerFolder newParentFolder)
		  {
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				string destinationFile = MakeRelativePath(false, newParentFolder.RelativeName, file.Name);
				FileOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFile, fileOperation.move, destinationFile);
		  }

		  public override void MoveFolder(FileManagerFolder folder, FileManagerFolder newParentFolder)
		  {
				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);
				FolderOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFolder, fileOperation.move, destinationFolder);
		  }

		  public override void RenameFile(FileManagerFile file, string name)
		  {
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				string destinationFile = MakeRelativePath(false, file.Folder.RelativeName, name);
				FileOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFile, fileOperation.rename, destinationFile);
		  }

		  public override void RenameFolder(FileManagerFolder folder, string name)
		  {

				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				string destinationFolder = MakeRelativePath(false, folder.Parent.RelativeName, name);
				FolderOperation(account.CreateCloudBlobClient(), BlobContainer, sourceFolder, fileOperation.rename, destinationFolder);
		  }

		  public override void UploadFile(FileManagerFolder folder, string fileName, Stream content)
		  {
				string blobReferencePath = MakeRelativePath(false, folder.RelativeName, fileName); //without starting '/'

				CloudBlobClient client = account.CreateCloudBlobClient();
				CloudBlobContainer container = client.GetContainerReference(BlobContainer);
				container.CreateIfNotExists(BlobContainerPublicAccessType.Container);

				CloudBlockBlob blob = container.GetBlockBlobReference(blobReferencePath);
				blob.UploadFromStream(content);
				ClearContainer(BlobContainer, client); //force rebuild
		  }

		  static readonly string[] thumbExtensions = new string[] { ".jpg", ".jpeg", ".gif", ".png" };

		  private static string GetThumbNailUrl(string thumbnailFolder, string accountRoot, string relativeUrl)
		  {
				string fileExtension = VirtualPathUtility.GetExtension(relativeUrl);
				if (thumbExtensions.Contains(fileExtension.ToLowerInvariant()))
				{
					 string thumbPath = MakeRelativePath(thumbnailFolder, "DBBlobCache", accountRoot, relativeUrl);
					 string path, name, ext, p;
					 if (ParseRelativePath(thumbPath, "/", out path, out name, out ext, out p))
					 {
						  // include rev in temp file to determine changed files
						  string r = String.Join("/", new string[] { path, String.Format("{0}{1}", name, ext) });
						  return r;
					 }
				}
				return String.Empty;
		  }

		  public static void InitializeFileManager(ASPxFileManager fileManager)
		  {
				fileManager.Settings.ThumbnailFolder = ThumbnailRootFolder;
				fileManager.CustomThumbnail += ASPxFileManager_CustomThumbnail;
				fileManager.FileDownloading += ASPxFileManager_FileDownloading;
		  }
		  static readonly object fileLock = new object();
		  public static void ASPxFileManager_CustomThumbnail(object source, FileManagerThumbnailCreateEventArgs e)
		  {
				ASPxFileManager fm = source as ASPxFileManager;
				if ((fm == null) || (fm.CustomFileSystemProvider == null) ||
					 (!typeof(BlobStorageFileSystemProvider).IsAssignableFrom(fm.CustomFileSystemProvider.GetType())))
					 return;


				string filePath = MakeRelativePath(e.File.RelativeName);

				BlobStorageFileSystemProvider provider = (BlobStorageFileSystemProvider)fm.CustomFileSystemProvider;
				string thumbUrl = GetThumbNailUrl(fm.Settings.ThumbnailFolder, String.Format("{0}/{1}", provider.account.Credentials.AccountName, BlobStorageFileSystemProvider.BlobContainer), filePath);
				// todo some cropping should occur here since dimensions are not used
				if (!String.IsNullOrEmpty(thumbUrl))
				{
					 e.ThumbnailImage.Width = Unit.Pixel(62);
					 e.ThumbnailImage.Height = Unit.Pixel(42);
					 e.ThumbnailImage.Url = thumbUrl;
				}
				//else
				//{
				//	 e.ThumbnailImage.Width = Unit.Pixel(48);
				//	 e.ThumbnailImage.Height = Unit.Pixel(48);
				//	 e.ThumbnailImage.Url = String.Format("~/Images/DropBoxTypes/48x48/{0}48.gif", fileItem.icon);
				//}
		  }

		  public static void ASPxFileManager_FileDownloading(object source, FileManagerFileDownloadingEventArgs e)
		  {
				ASPxFileManager fm = source as ASPxFileManager;
				if ((fm == null) || (fm.CustomFileSystemProvider == null) ||
					 (!typeof(BlobStorageFileSystemProvider).IsAssignableFrom(fm.CustomFileSystemProvider.GetType())))
					 return;

				string filePath = MakeRelativePath(false, e.File.RelativeName);

				BlobStorageFileSystemProvider provider = (BlobStorageFileSystemProvider)fm.CustomFileSystemProvider;

				CloudBlobClient client = provider.account.CreateCloudBlobClient();
				CloudBlobContainer container = client.GetContainerReference(BlobStorageFileSystemProvider.BlobContainer);
				container.CreateIfNotExists(BlobContainerPublicAccessType.Container);
				var blob = container.GetBlobReferenceFromServer(filePath);
				HttpContext.Current.Response.Redirect(blob.Uri.ToString());
		  }


		  #region Caching helper classes

		  class BlobFile
		  {
				public BlobFile(string fullPath, BlobFolder parent)
				{

					 Parent = parent;
					 FullPath = fullPath;
					 if (!String.IsNullOrEmpty(fullPath))
					 {
						  Path = MakeRelativePath(VirtualPathUtility.GetDirectory(fullPath));
						  Name = VirtualPathUtility.GetFileName(fullPath);
					 }
				}

				public BlobFolder Parent { get; protected set; }
				public string Name { get; protected set; }
				public string Path { get; protected set; }
				public string FullPath { get; protected set; }
		  }

		  class BlobFolder : BlobFile
		  {

				private readonly List<BlobFolder> _Folders = new List<BlobFolder>();
				private readonly List<BlobFile> _Files = new List<BlobFile>();

				public BlobFolder(string fullPath, BlobFolder parent)
					 : base(fullPath, parent)
				{

				}

				public List<BlobFolder> Folders { get { return _Folders; } }
				public List<BlobFile> Files { get { return _Files; } }
		  }

		  class BlobCache
		  {
				private readonly Dictionary<string, BlobFolder> _FolderList = new Dictionary<string, BlobFolder>();
				private readonly BlobFolder _RootFolder;

				public BlobCache(string container)
				{
					 _RootFolder = new BlobFolder(container, null);
					 _FolderList[container] = _RootFolder;
				}
				public BlobFolder RootFolder { get { return _RootFolder; } }
				protected BlobFolder AddFolder(string fullPath, BlobFolder parent)
				{

					 string path = MakeRelativePath(fullPath);
					 BlobFolder result = new BlobFolder(path, parent);
					 if ((parent != null) && (result != null))
						  parent.Folders.Add(result);

					 _FolderList[path] = result;
					 return result;
				}

				protected BlobFile AddFile(string fullPath, BlobFolder parent)
				{
					 BlobFile result = new BlobFile(fullPath, parent);
					 if ((parent != null) && (result != null))
						  parent.Files.Add(result);
					 return result;
				}

				public BlobFile AddItem(IListBlobItem blobItem, BlobFolder folder)
				{
					 if (folder == null)
						  folder = _RootFolder;

					 if (blobItem.GetType() == typeof(CloudBlobDirectory))
					 {
						  BlobFolder newFolder = AddFolder(blobItem.Uri.AbsolutePath, folder);
						  foreach (var item in ((CloudBlobDirectory)blobItem).ListBlobs(false))
						  {
								AddItem(item, newFolder);
						  }
						  return newFolder;
					 }
					 else
					 {
						  BlobFile result = null;
						  try
						  {
								if (VirtualPathUtility.GetFileName(blobItem.Uri.AbsolutePath) != DX_HIDDEN_FILE)
									 result = AddFile(blobItem.Uri.AbsolutePath, folder);
						  }
						  catch
						  {
								result = null;
						  }
						  return result;
					 }
				}


				public BlobFolder FindFolder(string path)
				{
					 if (_FolderList.ContainsKey(path))
						  return _FolderList[path];
					 return null;
				}

		  }
		  #endregion
	 }
}
