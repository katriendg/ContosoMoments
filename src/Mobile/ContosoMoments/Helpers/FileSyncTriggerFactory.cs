﻿using Microsoft.WindowsAzure.MobileServices;
using Microsoft.WindowsAzure.MobileServices.Files.Eventing;
using Microsoft.WindowsAzure.MobileServices.Files.Sync;
using Microsoft.WindowsAzure.MobileServices.Files.Sync.Triggers;
using Microsoft.WindowsAzure.MobileServices.Sync;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace ContosoMoments
{
    internal class FileSyncTriggerFactory : IFileSyncTriggerFactory
    {
        private readonly bool autoUpdateRecords;
        private readonly IMobileServiceClient mobileServiceClient;
        private readonly FileSyncHandler syncHandler;

        public FileSyncTriggerFactory(IMobileServiceClient mobileServiceClient, FileSyncHandler syncHandler, bool autoUpdateParentRecords)
        {
            if (mobileServiceClient == null) {
                throw new ArgumentNullException("mobileServiceClient");
            }

            this.mobileServiceClient = mobileServiceClient;
            this.syncHandler = syncHandler;
            this.autoUpdateRecords = autoUpdateParentRecords;
        }

        public IList<IFileSyncTrigger> CreateTriggers(IFileSyncContext fileSyncContext)
        {
            return new List<IFileSyncTrigger> {
                new CustomFileSyncTrigger(fileSyncContext, this.mobileServiceClient, this.syncHandler, this.autoUpdateRecords)
            };
        }
    }

    public sealed class CustomFileSyncTrigger : IFileSyncTrigger, IDisposable
    {
        private readonly IDisposable dataChangeNotificationSubscription;
        private readonly IDisposable fileChangeNotificationSubscription;
        private readonly IFileSyncContext fileSyncContext;
        private readonly IMobileServiceClient mobileServiceClient;
        private readonly FileSyncHandler syncHandler;

        public CustomFileSyncTrigger(IFileSyncContext fileSyncContext, IMobileServiceClient mobileServiceClient, FileSyncHandler syncHandler, bool autoUpdateParentRecords)
        {
            if (fileSyncContext == null) {
                throw new ArgumentNullException("fileSyncContext");
            }

            if (mobileServiceClient == null) {
                throw new ArgumentNullException("mobileServiceClient");
            }

            this.fileSyncContext = fileSyncContext;
            this.mobileServiceClient = mobileServiceClient;
            this.syncHandler = syncHandler;

            this.dataChangeNotificationSubscription = mobileServiceClient.EventManager.Subscribe<StoreOperationCompletedEvent>(OnStoreOperationCompleted);

            if (autoUpdateParentRecords) {
                this.fileChangeNotificationSubscription = mobileServiceClient.EventManager.Subscribe<FileOperationCompletedEvent>(OnFileOperationCompleted);
            }
        }

        ~CustomFileSyncTrigger()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposing) {
                if (this.dataChangeNotificationSubscription != null) {
                    this.dataChangeNotificationSubscription.Dispose();
                }

                if (this.fileChangeNotificationSubscription != null) {
                    this.fileChangeNotificationSubscription.Dispose();
                }
            }
        }

        private async void OnFileOperationCompleted(FileOperationCompletedEvent obj)
        {
            if (obj.Source == FileOperationSource.Local) {
                IMobileServiceSyncTable table = this.mobileServiceClient.GetSyncTable(obj.File.TableName);
                JObject item = await table.LookupAsync(obj.File.ParentId);

                if (item != null) {
                    await table.UpdateAsync(item);
                }
            }
        }

        private async void OnStoreOperationCompleted(StoreOperationCompletedEvent storeOperationEvent)
        {
            if (storeOperationEvent.Operation.TableName != "Image")
                return;

            switch (storeOperationEvent.Operation.Kind) {
                case LocalStoreOperationKind.Insert:
                case LocalStoreOperationKind.Update:
                case LocalStoreOperationKind.Upsert:
                    if (storeOperationEvent.Operation.Source == StoreOperationSource.ServerPull
                        || storeOperationEvent.Operation.Source == StoreOperationSource.ServerPush) {
                        await this.fileSyncContext.PullFilesAsync(storeOperationEvent.Operation.TableName, storeOperationEvent.Operation.RecordId);
                        await this.syncHandler.ProcessFilesAsync(storeOperationEvent.Operation.RecordId);
                    }
                    break;
                case LocalStoreOperationKind.Delete:
                    await this.fileSyncContext.MetadataStore.PurgeAsync(storeOperationEvent.Operation.TableName, storeOperationEvent.Operation.RecordId);
                    break;
                default:
                    break;
            }
        }
    }

}