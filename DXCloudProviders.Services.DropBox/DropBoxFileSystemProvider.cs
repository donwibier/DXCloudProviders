using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using System.Web.UI.WebControls;
using DevExpress.Utils.OAuth;
using DevExpress.Web;

namespace DXCloudProviders.Services.DropBox
{
	 public class DropBoxFileSystemProvider : FileSystemProviderBase
	 {
		  public DropBoxFileSystemProvider(string rootFolder)
				: base(rootFolder)
		  {
				Trace.WriteLine("DropBoxFileSystemProvider constructor called");
		  }

		  protected virtual DropBoxAuthentication.Dropbox_Me DropboxInfo
		  {
				get { return HttpContext.Current.Session[DropBoxAuthentication.SessionNameTicket] as DropBoxAuthentication.Dropbox_Me; }
		  }

		  public virtual IToken AccessToken
		  {
				get
				{
					 DropBoxAuthentication.Dropbox_Me info = DropboxInfo;
					 if (info != null)
						  return new Token(DropBoxAuthentication.Key, DropBoxAuthentication.Secret, info.AccessToken, "n/a");

					 //redirect to signin
					 if (HttpContext.Current != null)
					 {
						  HttpContext.Current.Session[DropBoxAuthentication.SessionNameReturnUrl] = HttpContext.Current.Request.Url.ToString();
						  Uri uri = new Uri(HttpContext.Current.Request.Url, VirtualPathUtility.ToAbsolute(DropBoxAuthentication.RedirectUrl));
						  HttpContext.Current.Response.Redirect(DropBoxAuthentication.GetAuthorizeUriForDropbox(uri));
						  return null;
					 }
					 else
						  throw new Exception("Authentication cannot be done in background thread");
				}
		  }
		  #region some static helper methods
		  public static readonly string ThumbnailRootFolder = "~/Temp/Thumbs/";

		  // Dropbox needs uppercase escape codes
		  // http://stackoverflow.com/questions/918019/net-urlencode-lowercase-problem
		  private static string UrlEncode(string s)
		  {
				char[] temp = HttpUtility.UrlEncode(s).Replace("+", "%20").ToCharArray();
				for (int i = 0; i < temp.Length - 2; i++)
				{
					 if (temp[i] == '%')
					 {
						  temp[i + 1] = char.ToUpper(temp[i + 1]);
						  temp[i + 2] = char.ToUpper(temp[i + 2]);
					 }
				}
				return new string(temp);
		  }
		  //static string MakeRelativePath(params string[] parts)
		  //{
		  //	 return MakeRelativePath(true, parts);
		  //}
		  static string MakeRelativePath(bool encode, params string[] parts)
		  {
				
				List<string> result = new List<string>();
				foreach (string p in parts)
					 result.AddRange(p.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries));
				
				if (encode){
					 for (int i = 0; i < result.Count; i++)
						  result[i] = UrlEncode(result[i]);
				}

				return String.Format("{0}{1}", (result.Count > 0 && result[0] == "~" ? "" : "/"), String.Join("/", result.ToArray()));
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

		  static string GetCacheKey(string tokenValue, string relativeFolder)
		  {
				return String.Format("{0}_{1}", tokenValue, relativeFolder);
		  }

		  #endregion

		  #region Dropbox communication helpers

		  static DropboxFolder GetContainer(string relativeFolder, Consumer oauth, IToken token)
		  {
				DropboxFolder container = HostingEnvironment.Cache[GetCacheKey(token.Value, relativeFolder)] as DropboxFolder;
				if (container == null)
				{
					 Trace.WriteLine(String.Format("GetFiles: Requesting '{0}' from Dropbox", relativeFolder));
					 try
					 {
						  string data = oauth.GetResource(new Uri("https://api.dropbox.com/1/metadata/dropbox" + relativeFolder, UriKind.Absolute), token, "2.0");
						  using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
						  {
								container = (DropboxFolder)new DataContractJsonSerializer(typeof(DropboxFolder)).ReadObject(stream);
								HostingEnvironment.Cache.Add(String.Format("{0}_{1}", token.Value, relativeFolder), container, null,
									 System.Web.Caching.Cache.NoAbsoluteExpiration, new TimeSpan(0, 4, 0), System.Web.Caching.CacheItemPriority.Normal, null);
								// fetch and check thumbnails in background task
								IToken t = new Token(token.ConsumerKey, token.ConsumerSecret, token.Value, token.Secret);
								Task.Factory.StartNew(() =>
								{
									 Trace.WriteLine("Checking thumbNails in Background Task");
									 foreach (var item in container.contents)
									 {
										  GetThumbNail(item, oauth, t, ThumbnailRootFolder);
									 }
								});
						  }
					 }
					 catch (Exception err)
					 {
						  string errText = err.Message;
						  if (errText.Contains("(401)") && (HttpContext.Current != null))
								DropBoxAuthentication.Signout(HttpContext.Current, HttpContext.Current.Request.Url.ToString());
						  else
						  {
								Trace.WriteLine(err.InnerException == null ? err.Message : err.InnerException.Message);
								container = null;
						  }
					 }
				}
				else
				{
					 Trace.WriteLine(String.Format("Getting '{0}' from cache", relativeFolder));
				}

				return container;
		  }

		  static readonly object fileLock = new object();

		  static void GetThumbNail(DropboxFile fileItem, Consumer oauth, IToken token, string thumbFolder)
		  {
				if (fileItem == null)
					 throw new ArgumentException("fileItem cannot be null");
				if (token == null)
					 throw new ArgumentException("token cannot be null");

				if (!fileItem.is_dir && fileItem.thumb_exists && (!String.IsNullOrEmpty(fileItem.path)))
				{
					 string thumbUrl = GetThumbNailUrl(thumbFolder, Thread.CurrentPrincipal.Identity.Name, fileItem);
					 if (String.IsNullOrEmpty(thumbUrl)) return;
					 string thumbPath = HostingEnvironment.MapPath(thumbUrl);
					 if (!File.Exists(thumbPath))
					 {
						  byte[] content = oauth.GetResourceBytes(new Uri(String.Format("https://api-content.dropbox.com/1/thumbnails/dropbox{0}?size=s", MakeRelativePath(true, fileItem.path)), UriKind.Absolute), token, "2.0");
						  if (content != null)
						  {
								using (MemoryStream ms = new MemoryStream(content))
								{
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

		  enum fileOperation { copy, move, delete, create_folder, rename };

		  static void FileOperation(Consumer consumer, IToken token, string item, fileOperation operation, string destination = "")
		  {
				if (consumer == null)
					 throw new ArgumentNullException("consumer");
				if (token == null)
					 throw new ArgumentNullException("token");
				if (String.IsNullOrEmpty(item))
					 throw new ArgumentNullException("item");
				try
				{
					 StringBuilder urlStr = new StringBuilder();
					 urlStr.AppendFormat("https://api.dropbox.com/1/fileops/{0}?root=dropbox", operation == fileOperation.rename ? fileOperation.move : operation);

					 switch (operation)
					 {
						  case fileOperation.copy:
						  case fileOperation.move:
								urlStr.AppendFormat("&from_path={0}&to_path={1}",
									 MakeRelativePath(true, item), MakeRelativePath(true, destination, VirtualPathUtility.GetFileName(item)));
								break;
						  case fileOperation.rename:
								urlStr.AppendFormat("&from_path={0}&to_path={1}",
									 MakeRelativePath(true, item), MakeRelativePath(true, VirtualPathUtility.GetDirectory(item), destination));
								break;
						  case fileOperation.delete:
						  case fileOperation.create_folder:
								urlStr.AppendFormat("&path={0}", MakeRelativePath(true, item));
								break;
					 }

					 string response = consumer.GetResource(new Uri(urlStr.ToString(), UriKind.Absolute), token, "2.0");
				}
				catch (Exception err)
				{
					 string errText = err.Message;
					 if (errText.Contains("(401)"))
					 {
						  DropBoxAuthentication.Signout(HttpContext.Current, HttpContext.Current.Request.Url.ToString());
						  return;
					 }
					 else if (errText.Contains("(403)"))
						  throw new Exception("Folder allready exists");
					 else
						  throw err;
				}
				// cleanup cache (and force refresh from dropbox)            
				string parent = VirtualPathUtility.RemoveTrailingSlash(VirtualPathUtility.GetDirectory(item));
				switch (operation)
				{
					 case fileOperation.copy:
					 case fileOperation.move:
						  HostingEnvironment.Cache.Remove(GetCacheKey(token.Value, destination));
						  break;
					 case fileOperation.delete:
						  DropboxFolder c = GetContainer(MakeRelativePath(false, parent), consumer, token);
						  if (c != null)
						  {
								DropboxFile f = c.contents.Find((x) => x.path == item);
								if (f != null)
									 c.contents.Remove(f);
						  }
						  break;
					 case fileOperation.create_folder:
					 case fileOperation.rename:
						  break;
				}
				HostingEnvironment.Cache.Remove(GetCacheKey(token.Value, parent));

		  }

		  static string GetFileShareUrl(Consumer consumer, IToken token, string item)
		  {
				if (consumer == null)
					 throw new ArgumentNullException("consumer");
				if (token == null)
					 throw new ArgumentNullException("token");
				if (String.IsNullOrEmpty(item))
					 throw new ArgumentNullException("item");
				try
				{
					 string data = consumer.GetResource(new Uri("https://api.dropbox.com/1/media/auto" + MakeRelativePath(true, item), UriKind.Absolute), token, "2.0");
					 using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
					 {
						  DropboxMediaInfo info = (DropboxMediaInfo)new DataContractJsonSerializer(typeof(DropboxMediaInfo)).ReadObject(stream);
						  return info.url;
					 }
				}
				catch (Exception err)
				{
					 string errText = err.Message;
					 if (errText.Contains("(401)") && (HttpContext.Current != null))
					 {
						  DropBoxAuthentication.Signout(HttpContext.Current, HttpContext.Current.Request.Url.ToString());
						  return null;
					 }
					 else if (errText.Contains("(403)"))
						  throw new Exception("Folder allready exists");
					 else
						  throw err;
				}
		  }


		  static void FileUpload(Consumer consumer, IToken token, string destinationFolder, string fileName, Stream content, int blockSize = 4096 * 1024)
		  {
				if (consumer == null)
					 throw new ArgumentNullException("consumer");
				if (token == null)
					 throw new ArgumentNullException("token");
				if (String.IsNullOrEmpty(fileName))
					 throw new ArgumentNullException("fileName");
				if (content == null)
					 throw new ArgumentNullException("content");

				string uploadID = string.Empty;
				int counter = 0;
				int offset = 0;
				int read = 0;
				byte[] buffer = new byte[blockSize];

				content.Position = 0;
				try
				{
					 while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
					 {
						  StringBuilder urlStr = new StringBuilder("https://api-content.dropbox.com/1/chunked_upload");
						  counter++;
						  if (counter > 1)
								urlStr.AppendFormat("?upload_id={0}&offset={1}", uploadID, offset);

						  string data = consumer.SubmitResource(new Uri(urlStr.ToString(), UriKind.Absolute), token, "2.0", buffer, read);
						  using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(data)))
						  {
								DropboxChunkedUploadInfo info = (DropboxChunkedUploadInfo)new DataContractJsonSerializer(typeof(DropboxChunkedUploadInfo)).ReadObject(stream);
								if (String.IsNullOrEmpty(uploadID))
									 uploadID = info.upload_id;
								offset = info.offset;
						  }
					 }
					 string commitUrl = String.Format("https://api-content.dropbox.com/1/commit_chunked_upload/dropbox{0}?upload_id={1}",
						  MakeRelativePath(true, destinationFolder, fileName), uploadID);

					 consumer.HttpMethod = "POST";
					 string commitData = consumer.GetResource(new Uri(commitUrl, UriKind.Absolute), token, "2.0");
				}
				catch (Exception err)
				{
					 string errText = err.Message;
					 if (errText.Contains("(401)") && (HttpContext.Current != null))
						  DropBoxAuthentication.Signout(HttpContext.Current, HttpContext.Current.Request.Url.ToString());
					 else
						  throw err;
				}
				HostingEnvironment.Cache.Remove(GetCacheKey(token.Value, destinationFolder));
		  }


		  #endregion

		  private Consumer _oauth = null;
		  protected Consumer oauth
		  {
				get
				{
					 if (_oauth == null)
						  _oauth = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url);
					 return _oauth;
				}
		  }

		  public override string RootFolderDisplayName { get { return String.Format("DropBox: {0}", DropboxInfo != null ? DropboxInfo.display_name : ""); } }


		  public override IEnumerable<FileManagerFile> GetFiles(FileManagerFolder folder)
		  {
				string relativePath = MakeRelativePath(false, folder.RelativeName);
				var files = from n in GetContainer(relativePath, oauth, AccessToken).contents
								where !n.is_dir
								select new FileManagerFile(this, folder, n.path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last());

				return files;
		  }

		  public override IEnumerable<FileManagerFolder> GetFolders(FileManagerFolder parentFolder)
		  {
				string relativePath = MakeRelativePath(false, parentFolder.RelativeName);
				var folders = from n in GetContainer(relativePath, oauth, AccessToken).contents
								  where n.is_dir
								  select new FileManagerFolder(this, parentFolder, n.path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Last());
				return folders;
		  }

		  public override Stream ReadFile(FileManagerFile file)
		  {
				return null;
		  }

		  public override bool Exists(FileManagerFile file)
		  {
				string relativeFile = MakeRelativePath(false, file.RelativeName);
				string relativeFolder = MakeRelativePath(false, file.Folder.RelativeName);
				var f = (from n in GetContainer(relativeFolder, oauth, AccessToken).contents
							where !n.is_dir && n.path == relativeFile
							select n).FirstOrDefault();

				return f != null;
		  }

		  public override bool Exists(FileManagerFolder folder)
		  {
				string relativePath = MakeRelativePath(false, folder.RelativeName);
				DropboxFolder f = GetContainer(relativePath, oauth, AccessToken);
				return f != null;
		  }

		  public override void CopyFile(FileManagerFile file, FileManagerFolder newParentFolder)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);
				FileOperation(o, AccessToken, sourceFile, fileOperation.copy, destinationFolder);
		  }

		  public override void CopyFolder(FileManagerFolder folder, FileManagerFolder newParentFolder)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);

				FileOperation(o, AccessToken, sourceFolder, fileOperation.copy, destinationFolder);
		  }

		  public override void CreateFolder(FileManagerFolder parent, string name)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string path = MakeRelativePath(false, parent.RelativeName, name);
				FileOperation(o, AccessToken, path, fileOperation.create_folder);
		  }

		  public override void DeleteFile(FileManagerFile file)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string path = MakeRelativePath(false, file.RelativeName);
				FileOperation(o, AccessToken, path, fileOperation.delete);
		  }

		  public override void DeleteFolder(FileManagerFolder folder)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string path = MakeRelativePath(false, folder.RelativeName);
				FileOperation(o, AccessToken, path, fileOperation.delete);
		  }


		  public override void MoveFile(FileManagerFile file, FileManagerFolder newParentFolder)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);
				FileOperation(o, AccessToken, sourceFile, fileOperation.move, destinationFolder);
		  }

		  public override void MoveFolder(FileManagerFolder folder, FileManagerFolder newParentFolder)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				string destinationFolder = MakeRelativePath(false, newParentFolder.RelativeName);
				FileOperation(o, AccessToken, sourceFolder, fileOperation.move, destinationFolder);
		  }

		  public override void RenameFile(FileManagerFile file, string name)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFile = MakeRelativePath(false, file.RelativeName);
				FileOperation(o, AccessToken, sourceFile, fileOperation.rename, name);
		  }

		  public override void RenameFolder(FileManagerFolder folder, string name)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				string sourceFolder = MakeRelativePath(false, folder.RelativeName);
				FileOperation(o, AccessToken, sourceFolder, fileOperation.rename, name);
		  }

		  public override void UploadFile(FileManagerFolder folder, string fileName, Stream content)
		  {
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "PUT");
				string destinationFolder = MakeRelativePath(false, folder.RelativeName);

				FileUpload(o, AccessToken, destinationFolder, fileName, content);
		  }


		  private static string GetThumbNailUrl(string thumbnailFolder, string accountRoot, DropBoxFileSystemProvider.DropboxFile fileItem)
		  {
				if (fileItem.thumb_exists)
				{
					 string thumbPath = MakeRelativePath(false, thumbnailFolder, "DBCache", accountRoot, fileItem.path);
					 string path, name, ext, p;
					 if (ParseRelativePath(thumbPath, "/", out path, out name, out ext, out p))
					 {
						  // include rev in temp file to determine changed files
						  string r = String.Join("/", new string[] { path, String.Format("{0}_{1}{2}", name, fileItem.rev, ext) });
						  return r;
					 }
				}
				return String.Empty;
		  }

		  public static void InitializeFileManager(DevExpress.Web.ASPxFileManager fileManager)
		  {
				fileManager.Settings.ThumbnailFolder = ThumbnailRootFolder;
				fileManager.CustomThumbnail += ASPxFileManager_CustomThumbnail;
				fileManager.FileDownloading += ASPxFileManager_FileDownloading;
		  }

		  public static void ASPxFileManager_CustomThumbnail(object source, FileManagerThumbnailCreateEventArgs e)
		  {
				DevExpress.Web.ASPxFileManager fm = source as DevExpress.Web.ASPxFileManager;
				if ((fm == null) || (fm.CustomFileSystemProvider == null) ||
					 (!typeof(DropBoxFileSystemProvider).IsAssignableFrom(fm.CustomFileSystemProvider.GetType())))
					 return;

				IToken token = ((DropBoxFileSystemProvider)fm.CustomFileSystemProvider).AccessToken;
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url);

				string filePath = MakeRelativePath(false, e.File.RelativeName);
				string folderPath = MakeRelativePath(false, e.File.Folder.RelativeName);
				DropboxFile fileItem = (from n in GetContainer(folderPath, o, token).contents
												where !n.is_dir && n.path == filePath
												select n).FirstOrDefault();

				if (fileItem == null)
					 return;

				string thumbUrl = GetThumbNailUrl(fm.Settings.ThumbnailFolder, Thread.CurrentPrincipal.Identity.Name, fileItem);
				// todo some cropping should occur here since dimensions are not used
				if (!String.IsNullOrEmpty(thumbUrl) && File.Exists(HostingEnvironment.MapPath(thumbUrl)))
				{
					 e.ThumbnailImage.Width = Unit.Pixel(62);
					 e.ThumbnailImage.Height = Unit.Pixel(42);
					 e.ThumbnailImage.Url = thumbUrl;
				}
				else
				{
					 e.ThumbnailImage.Width = Unit.Pixel(48);
					 e.ThumbnailImage.Height = Unit.Pixel(48);
					 e.ThumbnailImage.Url = String.Format("~/Images/DropBoxTypes/48x48/{0}48.gif", fileItem.icon);
				}
		  }

		  public static void ASPxFileManager_FileDownloading(object source, FileManagerFileDownloadingEventArgs e)
		  {
				DevExpress.Web.ASPxFileManager fm = source as DevExpress.Web.ASPxFileManager;
				if ((fm == null) || (fm.CustomFileSystemProvider == null) ||
					 (!typeof(DropBoxFileSystemProvider).IsAssignableFrom(fm.CustomFileSystemProvider.GetType())))
					 return;

				string filePath = MakeRelativePath(false, e.File.RelativeName);
				string folderPath = MakeRelativePath(false, e.File.Folder.RelativeName);

				IToken token = ((DropBoxFileSystemProvider)fm.CustomFileSystemProvider).AccessToken;
				Consumer o = DropBoxAuthentication.CreateConsumerForDropbox(HttpContext.Current.Request.Url, "POST");
				DropboxFile fileItem = (from n in GetContainer(folderPath, o, token).contents
												where !n.is_dir && n.path == filePath
												select n).FirstOrDefault();

				if (fileItem == null)
					 return;

				string url = GetFileShareUrl(o, token, filePath);
				if (String.IsNullOrEmpty(url))
					 throw new HttpException(404, "Item was not found");

				HttpContext.Current.Response.Redirect(url);
		  }

		  #region Helper classes for JSON deserialisation

		  [DataContract]
		  class DropboxFolder
		  {
				[DataMember]
				public string root { get; internal set; }
				[DataMember]
				public string path { get; internal set; }

				[DataMember]
				public string hash { get; internal set; }

				private List<DropboxFile> _contents;
				[DataMember]
				public List<DropboxFile> contents
				{
					 get
					 {
						  if (_contents == null)
								_contents = new List<DropboxFile>();
						  return _contents;
					 }
					 set { _contents = value; }
				}
		  }

		  [DataContract]
		  class DropboxFile
		  {
				[DataMember]
				public bool is_dir { get; internal set; }
				[DataMember]
				public string rev { get; internal set; }
				[DataMember]
				public string path { get; internal set; }
				[DataMember]
				public string root { get; internal set; }
				[DataMember]
				public string size { get; internal set; }
				[DataMember]
				public int bytes { get; internal set; }
				[DataMember()]
				public string icon { get; internal set; }
				[DataMember]
				public bool thumb_exists { get; internal set; }
				[DataMember]
				public string mime_type { get; internal set; }
		  }



		  [DataContract]
		  class DropboxChunkedUploadInfo
		  {
				[DataMember]
				public string upload_id { get; internal set; }

				[DataMember]
				public int offset { get; internal set; }

				[DataMember]
				public string expires { get; internal set; }
		  }

		  [DataContract]
		  class DropboxMediaInfo
		  {
				[DataMember]
				public string url { get; internal set; }

				[DataMember]
				public string expires { get; internal set; }
		  }

		  #endregion
	 }
}
