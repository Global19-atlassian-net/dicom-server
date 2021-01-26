﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Dicom.Core.Exceptions;
using Microsoft.Health.Dicom.Core.Features.CustomTag;
using Microsoft.Health.Dicom.SqlServer.Features.Schema;
using Microsoft.Health.Dicom.SqlServer.Features.Schema.Model;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;

namespace Microsoft.Health.Dicom.SqlServer.Features.ChangeFeed
{
    public class SqlCustomTagStore : ICustomTagStore
    {
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;
        private readonly SchemaInformation _schemaInformation;
        private readonly ILogger<SqlCustomTagStore> _logger;

        public SqlCustomTagStore(
           SqlConnectionWrapperFactory sqlConnectionWrapperFactory,
           SchemaInformation schemaInformation,
           ILogger<SqlCustomTagStore> logger)
        {
            EnsureArg.IsNotNull(sqlConnectionWrapperFactory, nameof(sqlConnectionWrapperFactory));
            EnsureArg.IsNotNull(schemaInformation, nameof(schemaInformation));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _sqlConnectionWrapperFactory = sqlConnectionWrapperFactory;
            _schemaInformation = schemaInformation;
            _logger = logger;
        }

        public async Task AddCustomTagsAsync(IEnumerable<CustomTagEntry> customTagEntries, CancellationToken cancellationToken = default)
        {
            if (_schemaInformation.Current < SchemaVersionConstants.SupportCustomTagSchemaVersion)
            {
                throw new BadRequestException(DicomSqlServerResource.SchemaVersionNeedsToBeUpgraded);
            }

            using (SqlConnectionWrapper sqlConnectionWrapper = await _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(cancellationToken))
            using (SqlCommandWrapper sqlCommandWrapper = sqlConnectionWrapper.CreateSqlCommand())
            {
                IEnumerable<AddCustomTagsInputTableTypeV1Row> rows = customTagEntries.Select(entry => ToAddCustomTagsInputTableTypeV1Row(entry));
                V2.AddCustomTags.PopulateCommand(sqlCommandWrapper, new V2.AddCustomTagsTableValuedParameters(rows));

                try
                {
                    await sqlCommandWrapper.ExecuteScalarAsync(cancellationToken);
                }
                catch (SqlException ex)
                {
                    switch (ex.Number)
                    {
                        case SqlErrorCodes.Conflict:
                            throw new CustomTagAlreadyExistsException();

                        default:
                            throw new DataStoreException(ex);
                    }
                }
            }
        }

        private static AddCustomTagsInputTableTypeV1Row ToAddCustomTagsInputTableTypeV1Row(CustomTagEntry entry)
        {
            // TODO: CustomTagEntry should support status?
            return new AddCustomTagsInputTableTypeV1Row(entry.Path, entry.VR, (byte)entry.Level, Status: (byte)CustomTagStatus.Reindexing);
        }
    }
}