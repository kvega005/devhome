﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Security;
using HyperVExtension.CommunicationWithGuest;
using HyperVExtension.Helpers;
using HyperVExtension.Providers;
using Microsoft.Windows.DevHome.SDK;
using Windows.Foundation;

namespace HyperVExtension.Models;

internal sealed class WaitForLoginAdaptiveCardSession : IExtensionAdaptiveCardSession2, IDisposable
{
    private sealed class InputPayload
    {
        public string? Id
        {
            get; set;
        }
    }

    private readonly ApplyConfigurationOperation _operation;
    private readonly ManualResetEvent _sessionStatusChangedEvent = new(false);
    private IExtensionAdaptiveCard? _extensionAdaptiveCard;
    private bool _isUserLoggedIn;
    private bool _disposed;

    public event TypedEventHandler<IExtensionAdaptiveCardSession2, ExtensionAdaptiveCardSessionData>? SessionStatusChanged;

    public WaitForLoginAdaptiveCardSession(ApplyConfigurationOperation operation)
    {
        _operation = operation;
    }

    void IExtensionAdaptiveCardSession.Dispose()
    {
        Logging.Logger()?.ReportDebug($"Dispose");
        ((IDisposable)this).Dispose();
    }

    public ProviderOperationResult Initialize(IExtensionAdaptiveCard extensionAdaptiveCard)
    {
        Logging.Logger()?.ReportDebug($"Initialize");
        _extensionAdaptiveCard = extensionAdaptiveCard;
        var operationResult = _extensionAdaptiveCard.Update(GetTemplate(), null, "WaitForVmUserLogin");
        SessionStatusChanged?.Invoke(this, new ExtensionAdaptiveCardSessionData(ExtensionAdaptiveCardSessionEventKind.SessionStarted, operationResult));
        return operationResult;
    }

    public IAsyncOperation<ProviderOperationResult> OnAction(string action, string inputs)
    {
        return Task.Run(() =>
        {
            ProviderOperationResult operationResult;
            Logging.Logger()?.ReportInfo($"OnAction() called with state:{_extensionAdaptiveCard?.State}");
            Logging.Logger()?.ReportDebug($"action: {action}");

            switch (_extensionAdaptiveCard?.State)
            {
                case "WaitForVmUserLogin":
                    {
                        Logging.Logger()?.ReportDebug($"inputs: {inputs}");
                        var actionPayload = Json.ToObject<AdaptiveCardActionPayload>(action) ?? throw new InvalidOperationException("Invalid action");
                        if (actionPayload.IsOkAction())
                        {
                            _isUserLoggedIn = true;
                        }

                        operationResult = new ProviderOperationResult(ProviderOperationStatus.Success, null, null, null);
                        _sessionStatusChangedEvent.Set();
                        break;
                    }

                default:
                    {
                        Logging.Logger()?.ReportError($"Unexpected state:{_extensionAdaptiveCard?.State}");
                        operationResult = new ProviderOperationResult(ProviderOperationStatus.Failure, null, "Something went wrong", $"Unexpected state:{_extensionAdaptiveCard?.State}");
                        break;
                    }
            }

            SessionStatusChanged?.Invoke(this, new ExtensionAdaptiveCardSessionData(ExtensionAdaptiveCardSessionEventKind.SessionStarted, operationResult));
            return operationResult;
        }).AsAsyncOperation();
    }

    public bool WaitForUserResponse()
    {
        WaitHandle.WaitAny(new[] { _sessionStatusChangedEvent, _operation.CancellationToken.WaitHandle });
        return _isUserLoggedIn;
    }

    void IDisposable.Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _sessionStatusChangedEvent?.Dispose();
            }

            _disposed = true;
        }
    }

    private string GetTemplate()
    {
        return Resources.ReplaceIdentifers(_credentialUITemplate, Resources.GetWidgetResourceIdentifiers(), Logging.Logger());
    }

    private static readonly string _credentialUITemplate = @"
{
    ""type"": ""AdaptiveCard"",
    ""$schema"": ""http://adaptivecards.io/schemas/adaptive-card.json"",
    ""version"": ""1.5"",
    ""body"": [
        {
            ""type"": ""TextBlock"",
            ""text"": ""%WaitForLoginRequest.Title%"",
            ""wrap"": true,
            ""style"": ""heading""
        },
        {
            ""type"": ""TextBlock"",
            ""text"": ""%WaitForLoginRequest.Description%"",
            ""wrap"": true
        }
    ],
    ""actions"": [
        {
            ""type"": ""Action.Execute"",
            ""title"": ""%VmCredentialRequest.OkText%"",
            ""data"": {
                ""id"": ""okAction""
            },
            ""id"": ""okAction""
        },
        {
            ""type"": ""Action.Execute"",
            ""title"": ""%VmCredentialRequest.CancelText%"",
            ""data"": {
                ""id"": ""cancelAction""
            },
            ""id"": ""cancelAction""
        }
    ]
}
";
}