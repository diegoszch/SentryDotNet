﻿namespace SentryDotNet
{
    /// <summary>
    /// Describes the SDK used to capture and transmit the event.
    /// <para />
    /// https://docs.sentry.io/clientdev/interfaces/sdk/
    /// </summary>
    public class SentrySdk
    {
        public static readonly SentrySdk SentryDotNet = new SentrySdk
        {
            Name = "SentryDotNet",
            Version = typeof(SentryClient).Assembly.GetName().Version.ToString(3)
        }; 
        
        /// <summary>
        /// The name of the SDK.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The version of the SDK.
        /// </summary>
        public string Version { get; set; }
    }
}