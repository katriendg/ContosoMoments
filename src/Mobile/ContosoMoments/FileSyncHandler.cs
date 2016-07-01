using Microsoft.WindowsAzure.MobileServices.Files;
using Microsoft.WindowsAzure.MobileServices.Files.Metadata;
using Microsoft.WindowsAzure.MobileServices.Files.Sync;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Xamarin.Forms;
using System.Linq;

namespace ContosoMoments
{
    public class FileSyncHandler : IFileSyncHandler
    {
        private readonly App theApp;

        public FileSyncHandler(App app)
        {
            this.theApp = app;
        }

        public Task<IMobileServiceFileDataSource> GetDataSource(MobileServiceFileMetadata metadata)
        {
            IPlatform platform = DependencyService.Get<IPlatform>();
            return platform.GetFileDataSource(metadata);
        }

        public async Task ProcessFileSynchronizationAction(MobileServiceFile file, FileSynchronizationAction action)
        {
            try {
                // Process only file deletes. File create and update are processed in bulk, so that
                // we can choose what image size to download among those available
                if (action == FileSynchronizationAction.Delete) {
                    await FileHelper.DeleteLocalFileAsync(file, theApp.DataFilesPath);
                }
            }
            catch (Exception e) { // should catch WrappedStorageException, but this type is internal in the Storage SDK!
                Trace.WriteLine("Exception while downloading blob, blob probably does not exist: " + e);
            }
        }

        public async Task ProcessFilesAsync(string id)
        {
            Debug.WriteLine($"ProcessFilesAsync for record: {id}");

            var platform = DependencyService.Get<IPlatform>();

            var item = await theApp.imageTableSync.LookupAsync(id);
            var files = await theApp.imageTableSync.GetFilesAsync<Models.Image>(item);

            // download medium image if it exists, otherwise download the large image
            // the large image has no size prefix, so just exclude "sm" and "xs"
            var toDownload =
                files.Where(f => f.Name.StartsWith("md-")).FirstOrDefault() ??
                files.Where(f => !f.Name.StartsWith("sm-") && !f.Name.StartsWith("xs-")).FirstOrDefault(); 

            if (toDownload != null) {
                await this.theApp.DownloadFileAsync(toDownload);
            }
        }
    }
}
