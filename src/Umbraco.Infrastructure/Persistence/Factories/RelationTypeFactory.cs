﻿using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Infrastructure.Persistence.Dtos;

namespace Umbraco.Cms.Infrastructure.Persistence.Factories
{
    internal static class RelationTypeFactory
    {
        #region Implementation of IEntityFactory<RelationType,RelationTypeDto>

        public static IRelationType BuildEntity(RelationTypeDto dto)
        {
            var entity = new RelationType(dto.Name, dto.Alias, dto.Dual, dto.ParentObjectType, dto.ChildObjectType);

            try
            {
                entity.DisableChangeTracking();

                entity.Id = dto.Id;
                entity.Key = dto.UniqueId;

                // reset dirty initial properties (U4-1946)
                entity.ResetDirtyProperties(false);
                return entity;
            }
            finally
            {
                entity.EnableChangeTracking();
            }
        }

        public static RelationTypeDto BuildDto(IRelationType entity)
        {
            var dto = new RelationTypeDto
            {
                Alias = entity.Alias,
                ChildObjectType = entity.ChildObjectType,
                Dual = entity.IsBidirectional,
                Name = entity.Name,
                ParentObjectType = entity.ParentObjectType,
                UniqueId = entity.Key
            };
            if (entity.HasIdentity)
            {
                dto.Id = entity.Id;
            }

            return dto;
        }

        #endregion
    }
}
