using System;
using Newtonsoft.Json;
using System.IO;
using System.Linq;
using System.Net;

namespace GDriveSiteDeployCli
{
    class MainClass
    {
        static void Usage()
        {
            Console.Error.WriteLine("Google Drive Site Deployment Client");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("Usage:");
            Console.Error.WriteLine("   GDriveSiteDeployCli <source-path> [target-path]");
            Console.Error.WriteLine("");
            Console.Error.WriteLine("source-path   Path on local file system, where site is located");
            Console.Error.WriteLine("target-path   Path on Google Drive, where files would be pushed to");
            Console.Error.WriteLine("");
        }

        static string FindClientSecretPath()
        {
            var clientSecretPath = "client_secret.json";

            if (!System.IO.File.Exists(clientSecretPath))
            {
                var exePath = typeof(MainClass).Assembly.Location;
                clientSecretPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exePath), "client_secret.json");
            }

            if (!System.IO.File.Exists(clientSecretPath))
            {
                clientSecretPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GDriveSiteDeployCli", "client_secret.json");
            }

            if (!System.IO.File.Exists(clientSecretPath))
            {
                clientSecretPath = System.IO.Path.Combine(System.IO.Path.DirectorySeparatorChar.ToString(), "etc", "GDriveSiteDeployCli", "client_secret.json");
            }

            if (!System.IO.File.Exists(clientSecretPath))
            {
                throw new Exception("Cannot find file: 'client_secret.json'. You need to create this file using instructions on https://developers.google.com/drive/web/about-sdk");
            }

            return clientSecretPath;
        }


        public static int Main(string[] args)
        {
            var helpOptions = "--help,-help,/?,-h,-?";
            if (args.Length == 0 || args.Length > 2 || helpOptions.Split(',').Contains(args[0].ToLowerInvariant()))
            {
                Usage();
                return 1;
            }

            try
            {
                var localfolderInfo = new DirectoryInfo(args[0]);
                var folderName = localfolderInfo.FullName;
                if (!localfolderInfo.Exists)
                {
                    throw new Exception("Folder does not exist: " + localfolderInfo.FullName);
                }

                var webFolderName = (args.Length == 2) ? args[1] : System.IO.Path.Combine("/static-web-hosting",  localfolderInfo.Name);

                var gsi = new GoogleServiceInitializer(FindClientSecretPath());
                var service = gsi.CreateDriveService().GetAwaiter().GetResult();

                var pf = new PublicFolder(service);
                var folderId = pf.Setup(webFolderName).GetAwaiter().GetResult();
                pf.SyncFolder(folderName, folderId).Wait();

                var f = service.Files.Get(folderId).Execute();
                Console.WriteLine(f.WebViewLink);

                return 0;
            }
            catch(Exception ex)
            {
                #if DEBUG
                Console.Error.WriteLine("Error: {0}", ex);
                #else
                Console.Error.WriteLine("Error: {0}", ex.Message);
                #endif

                var webEx = ex as WebException;
                if (webEx != null && webEx.Status == WebExceptionStatus.SendFailure)
                {
                    Console.Error.WriteLine("This error may be fixable by running 'mozroots --import'");
                }

                return 2;
            }
        }
    }
}
