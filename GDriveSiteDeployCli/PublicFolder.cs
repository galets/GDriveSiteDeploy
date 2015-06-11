using System;
using System.Linq;
using Google.Apis.Drive.v2;
using Google.Apis.Drive.v2.Data;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GDriveSiteDeployCli
{
    public class PublicFolder
    {
        private readonly string MIME_FOLDER = "application/vnd.google-apps.folder";
        private readonly string Q_FOLDER = "(mimeType = 'application/vnd.google-apps.folder' and trashed = false)";
        private readonly string Q_FILE = "(mimeType != 'application/vnd.google-apps.folder' and trashed = false)";

        DriveService service;
            
        public PublicFolder(DriveService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            this.service = service;
        }

        static string _q(string text)
        {
            return "'" + text.Replace("'", "''") + "'";
        }

        async Task<ChildList> ListFolder(string folderId, string query = null)
        {
            var files = service.Children.List(folderId);
            files.Q = query;
            return await files.ExecuteAsync();
        }

        async Task<File> CreateFolder(string parentFolderId, string name)
        {
            var folder = await service.Files.Insert(new File()
            {
                Title = name,
                MimeType = MIME_FOLDER,
                Parents = new[] { new ParentReference() { Id = parentFolderId } }.ToList(),
            }).ExecuteAsync();

            return folder;
        }

        async Task UploadFile(string parentFolderId, string name, string filePath)
        {
            using (var stream = System.IO.File.OpenRead(filePath))
            {
                var fileUploadProgress = await service.Files.Insert(new File()
                {
                    Title = name,
                    Parents = new[] { new ParentReference() { Id = parentFolderId } }.ToList(),
                }, stream, MimeTypes.MimeTypeMap.GetMimeType(System.IO.Path.GetExtension(filePath))).UploadAsync();

            }
        }

        string Md5(string filePath)
        {
            using (var md5provider = new System.Security.Cryptography.MD5CryptoServiceProvider())
            using (var stream = System.IO.File.OpenRead(filePath))
            {
                var hash = md5provider.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public async Task<string> Setup(string folderName)
        {
            var pathList = new List<string>();
            for (var p = folderName; p != "" && p != "/" && p != "\\" && p != null; p = System.IO.Path.GetDirectoryName(p))
            {
                pathList.Insert(0, System.IO.Path.GetFileName(p));
            }

            var folderId = (await service.About.Get().ExecuteAsync()).RootFolderId;
            foreach (var name in pathList)
            {
                var fileList = await ListFolder(folderId, string.Format("{0} and title = {1}", Q_FOLDER, _q(name)));

                if (fileList.Items.Any())
                {
                    folderId = fileList.Items.First().Id;
                }
                else
                {
                    var folder = await CreateFolder(folderId, name);
                    folderId = folder.Id;
                }
            }

            var permission = new Permission()
            {
                Value = "",
                Type = "anyone",
                Role = "reader"
            };

            await service.Permissions.Insert(permission, folderId).ExecuteAsync();

            return folderId;
        }

        private async Task DoSyncFolder(string prefix, string localFolderPath, string gdriveFolderId)
        {
            var localFiles = (new System.IO.DirectoryInfo(localFolderPath)).EnumerateFiles();
            var remoteFiles = (await ListFolder(gdriveFolderId, Q_FILE)).Items.Select(i => service.Files.Get(i.Id).Execute()).ToList();

            var allFiles = new HashSet<string>(localFiles.Select(f => f.Name).Concat(remoteFiles.Select(r => r.Title)));
            foreach(var f in allFiles)
            {
                var lf = localFiles.Where(f1 => f1.Name == f).FirstOrDefault();
                var rf = remoteFiles.Where(f1 => f1.Title == f).FirstOrDefault();

                if (lf != null)
                {
                    if (rf != null)
                    {
                        var md5 = Md5(System.IO.Path.Combine(localFolderPath, f));
                        if (md5 == rf.Md5Checksum)
                        {
                            Console.Error.WriteLine("{0}: {1}/{2}", "Skip", prefix, f);
                        }
                        else
                        {
                            Console.Error.WriteLine("{0}: {1}/{2}", "Update", prefix, f);

                            await service.Files.Delete(rf.Id).ExecuteAsync();
                            await UploadFile(gdriveFolderId, f, System.IO.Path.Combine(localFolderPath, f));
                        }
                    }
                    else
                    {
                        Console.Error.WriteLine("{0}: {1}/{2}", "Upload", prefix, f);

                        await UploadFile(gdriveFolderId, f, System.IO.Path.Combine(localFolderPath, f));
                    }
                }
                else
                {
                    if (rf != null)
                    {
                        Console.Error.WriteLine("{0}: {1}/{2}", "Delete", prefix, f);
                        await service.Files.Delete(rf.Id).ExecuteAsync();
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }

            var localFolders = (new System.IO.DirectoryInfo(localFolderPath)).EnumerateDirectories().Where(f => f.Name != "." && f.Name != ".." ).ToArray();
            var remoteFolders = (await ListFolder(gdriveFolderId, Q_FOLDER)).Items.Select(i => service.Files.Get(i.Id).Execute()).ToList();
            var allFolders = new HashSet<string>(localFolders.Select(f => f.Name).Concat(remoteFolders.Select(r => r.Title)));
            var foldersToRecurseInto = new HashSet<Tuple<string, string>>();

            foreach (var f in allFolders)
            {
                var lf = localFolders.Where(f1 => f1.Name == f).FirstOrDefault();
                var rf = remoteFolders.Where(f1 => f1.Title == f).FirstOrDefault();

                if (lf != null)
                {
                    if (rf != null)
                    {
                        foldersToRecurseInto.Add(Tuple.Create(f, rf.Id));
                    }
                    else
                    {
                        Console.Error.WriteLine("{0}: {1}/{2}", "Create Folder", prefix, f);

                        var folder = await CreateFolder(gdriveFolderId, f);
                        foldersToRecurseInto.Add(Tuple.Create(f, folder.Id));
                    }
                }
                else
                {
                    if (rf != null)
                    {
                        Console.Error.WriteLine("{0}: {1}/{2}", "Delete Folder", prefix, f);
                        await service.Files.Delete(rf.Id).ExecuteAsync();
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }

            foreach (var f in foldersToRecurseInto)
            {
                Console.Error.WriteLine("{0}: {1}/{2}", "Sync Folder", prefix, f.Item1);
                await DoSyncFolder(prefix + "/" + f.Item1, System.IO.Path.Combine(localFolderPath, f.Item1), f.Item2);
            }
        }

        public Task SyncFolder(string localFolderPath, string gdriveFolderId)
        {
            return DoSyncFolder("", localFolderPath, gdriveFolderId);
        }
    }
}

