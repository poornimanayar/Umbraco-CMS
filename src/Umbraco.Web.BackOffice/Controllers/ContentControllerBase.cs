﻿using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Umbraco.Core;
using Umbraco.Core.Dictionary;
using Umbraco.Core.Events;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Editors;
using Umbraco.Core.PropertyEditors;
using Umbraco.Core.Serialization;
using Umbraco.Core.Services;
using Umbraco.Core.Strings;
using Umbraco.Extensions;
using Umbraco.Web.Common.Exceptions;
using Umbraco.Web.Common.Filters;
using Umbraco.Web.Models.ContentEditing;

namespace Umbraco.Web.BackOffice.Controllers
{
    /// <summary>
    /// An abstract base controller used for media/content/members to try to reduce code replication.
    /// </summary>
    [JsonDateTimeFormat]
    public abstract class ContentControllerBase : BackOfficeNotificationsController
    {
        protected ICultureDictionary CultureDictionary { get; }
        protected ILoggerFactory LoggerFactory { get; }
        protected IShortStringHelper ShortStringHelper { get; }
        protected IEventMessagesFactory EventMessages { get; }
        protected ILocalizedTextService LocalizedTextService { get; }
        private readonly ILogger<ContentControllerBase> _logger;
        private readonly IJsonSerializer _serializer;

        protected ContentControllerBase(
            ICultureDictionary cultureDictionary,
            ILoggerFactory loggerFactory,
            IShortStringHelper shortStringHelper,
            IEventMessagesFactory eventMessages,
            ILocalizedTextService localizedTextService,
            IJsonSerializer serializer)
        {
            CultureDictionary = cultureDictionary;
            LoggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<ContentControllerBase>();
            ShortStringHelper = shortStringHelper;
            EventMessages = eventMessages;
            LocalizedTextService = localizedTextService;
            _serializer = serializer;
        }

        /// <summary>
        /// Handles if the content for the specified ID isn't found
        /// </summary>
        /// <param name="id">The content ID to find</param>
        /// <param name="throwException">Whether to throw an exception</param>
        /// <returns>The error response</returns>
        protected NotFoundObjectResult HandleContentNotFound(object id, bool throwException = true)
        {
            ModelState.AddModelError("id", $"content with id: {id} was not found");
            NotFoundObjectResult errorResponse = NotFound(ModelState);

            if (throwException)
            {
                throw new HttpResponseException(errorResponse);
            }

            return errorResponse;
        }

        /// <summary>
        /// Maps the dto property values to the persisted model
        /// </summary>
        internal void MapPropertyValuesForPersistence<TPersisted, TSaved>(
            TSaved contentItem,
            ContentPropertyCollectionDto dto,
            Func<TSaved, IProperty, object> getPropertyValue,
            Action<TSaved, IProperty, object> savePropertyValue,
            string culture)
            where TPersisted : IContentBase
            where TSaved : IContentSave<TPersisted>
        {
            // map the property values
            foreach (ContentPropertyDto propertyDto in dto.Properties)
            {
                // get the property editor
                if (propertyDto.PropertyEditor == null)
                {
                    _logger.LogWarning("No property editor found for property {PropertyAlias}", propertyDto.Alias);
                    continue;
                }

                // get the value editor
                // nothing to save/map if it is readonly
                IDataValueEditor valueEditor = propertyDto.PropertyEditor.GetValueEditor();
                if (valueEditor.IsReadOnly)
                {
                    continue;
                }

                // get the property
                IProperty property = contentItem.PersistedContent.Properties[propertyDto.Alias];

                // prepare files, if any matching property and culture
                ContentPropertyFile[] files = contentItem.UploadedFiles
                    .Where(x => x.PropertyAlias == propertyDto.Alias && x.Culture == propertyDto.Culture && x.Segment == propertyDto.Segment)
                    .ToArray();

                foreach (ContentPropertyFile file in files)
                {
                    file.FileName = file.FileName.ToSafeFileName(ShortStringHelper);
                }

                // create the property data for the property editor
                var data = new ContentPropertyData(propertyDto.Value, propertyDto.DataType.Configuration)
                {
                    ContentKey = contentItem.PersistedContent.Key,
                    PropertyTypeKey = property.PropertyType.Key,
                    Files = files
                };

                // let the editor convert the value that was received, deal with files, etc
                object value = valueEditor.FromEditor(data, getPropertyValue(contentItem, property));

                // set the value - tags are special
                TagsPropertyEditorAttribute tagAttribute = propertyDto.PropertyEditor.GetTagAttribute();
                if (tagAttribute != null)
                {
                    TagConfiguration tagConfiguration = ConfigurationEditor.ConfigurationAs<TagConfiguration>(propertyDto.DataType.Configuration);
                    if (tagConfiguration.Delimiter == default)
                    {
                        tagConfiguration.Delimiter = tagAttribute.Delimiter;
                    }

                    var tagCulture = property.PropertyType.VariesByCulture() ? culture : null;
                    property.SetTagsValue(_serializer, value, tagConfiguration, tagCulture);
                }
                else
                {
                    savePropertyValue(contentItem, property, value);
                }
            }
        }

        /// <summary>
        /// Handles if the state is invalid
        /// </summary>
        /// <param name="display">The model state to display</param>
        protected virtual void HandleInvalidModelState(IErrorModel display)
        {
            // lastly, if it is not valid, add the model state to the outgoing object and throw a 403
            if (!ModelState.IsValid)
            {
                display.Errors = ModelState.ToErrorDictionary();
                throw HttpResponseException.CreateValidationErrorResponse(display);
            }
        }

        /// <summary>
        /// A helper method to attempt to get the instance from the request storage if it can be found there,
        /// otherwise gets it from the callback specified
        /// </summary>
        /// <typeparam name="TPersisted"></typeparam>
        /// <param name="getFromService"></param>
        /// <returns></returns>
        /// <remarks>
        /// This is useful for when filters have already looked up a persisted entity and we don't want to have
        /// to look it up again.
        /// </remarks>
        protected TPersisted GetObjectFromRequest<TPersisted>(Func<TPersisted> getFromService)
        {
            // checks if the request contains the key and the item is not null, if that is the case, return it from the request, otherwise return
            // it from the callback
            return HttpContext.Items.ContainsKey(typeof(TPersisted).ToString()) && HttpContext.Items[typeof(TPersisted).ToString()] != null
                ? (TPersisted)HttpContext.Items[typeof(TPersisted).ToString()]
                : getFromService();
        }

        /// <summary>
        /// Returns true if the action passed in means we need to create something new
        /// </summary>
        /// <param name="action">The content action</param>
        /// <returns>Returns true  if this is a creating action</returns>
        internal static bool IsCreatingAction(ContentSaveAction action) => action.ToString().EndsWith("New");

        /// <summary>
        /// Adds a cancelled message to the display
        /// </summary>
        /// <param name="display"></param>
        /// <param name="header"></param>
        /// <param name="message"></param>
        /// <param name="localizeHeader"></param>
        /// <param name="localizeMessage"></param>
        /// <param name="headerParams"></param>
        /// <param name="messageParams"></param>
        protected void AddCancelMessage(INotificationModel display, string header = "speechBubbles/operationCancelledHeader", string message = "speechBubbles/operationCancelledText", bool localizeHeader = true,
            bool localizeMessage = true,
            string[] headerParams = null,
            string[] messageParams = null)
        {
            // if there's already a default event message, don't add our default one
            IEventMessagesFactory messages = EventMessages;
            if (messages != null && messages.GetOrDefault().GetAll().Any(x => x.IsDefaultEventMessage))
            {
                return;
            }

            display.AddWarningNotification(
                localizeHeader ? LocalizedTextService.Localize(header, headerParams) : header,
                localizeMessage ? LocalizedTextService.Localize(message, messageParams) : message);
        }
    }
}
