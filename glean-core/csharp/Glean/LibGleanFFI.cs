﻿// This Source Code Form is subject to the terms of the Mozilla Public
// License, v. 2.0. If a copy of the MPL was not distributed with this
// file, You can obtain one at http://mozilla.org/MPL/2.0/.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Mozilla.Glean.FFI
{
    /// <summary>
    /// Result values of attempted ping uploads encoded for FFI use.
    /// They are defined in `glean-core/src/upload/result.rs` and re-defined for use in Kotlin here.
    /// 
    /// NOTE:
    /// THEY MUST BE THE SAME ACROSS BOTH FILES!
    /// </summary>
    internal enum Constants : int
    {
        // A recoverable error.
        UPLOAD_RESULT_RECOVERABLE = 0x1,

        // An unrecoverable error.
        UPLOAD_RESULT_UNRECOVERABLE = 0x2,

        // A HTTP response code.
        UPLOAD_RESULT_HTTP_STATUS = 0x8000
    }

    /// <summary>
    /// Rust represents the upload task as an Enum
    /// and to go through the FFI that gets transformed into a tagged union.
    /// Each variant is represented as an 8-bit unsigned integer.
    ///
    /// This *MUST* have the same order as the variants in `glean-core/ffi/src/upload.rs`.
    /// </summary>
    enum UploadTaskTag : int
    {
        Upload,
        Wait,
        Done
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct FfiUploadTaskBody
    {
        public byte tag;
        public IntPtr documentId;
        public IntPtr path;
        public int bodyLen;
        public IntPtr body;
        public IntPtr headers;
    }

    /// <summary>
    /// Represent an upload task by simulating the union passed through
    /// the FFI layer.
    /// </summary>
    [StructLayout(LayoutKind.Explicit)]
    internal struct FfiUploadTask
    {
        [FieldOffset(0)]
        public byte tag;
        [FieldOffset(0), MarshalAs(UnmanagedType.Struct)]
        public FfiUploadTaskBody body;
    }

    internal static class LibGleanFFI
    {
        private const string SharedGleanLibrary = "glean_ffi";

        // Define the order of fields as laid out in memory.
        // **CAUTION**: This must match _exactly_ the definition on the Rust side.
        //  If this side is changed, the Rust side need to be changed, too.
        [StructLayout(LayoutKind.Sequential)]
        internal class FfiConfiguration
        {
            public string data_dir;
            public string package_name;
            public bool upload_enabled;
            public Int32? max_events;
            public bool delay_ping_lifetime_io;
        }

        /// <summary>
        /// A base handle class meant to be extended by the different metric types to allow
        /// for calling metric specific clearing functions.
        /// </summary>
        internal class BaseGleanHandle : SafeHandle
        {
            public BaseGleanHandle() : base(invalidHandleValue: IntPtr.Zero, ownsHandle: true) { }

            public override bool IsInvalid
            {
                get { return this.handle == IntPtr.Zero; }
            }

            protected override bool ReleaseHandle()
            {
                // Note: this is meant to be implemented by the inheriting class in order to
                // provide a specific cleanup action.
                return false;
            }
        }

        public static string GetFromRustString(IntPtr pointer)
        {
            int len = 0;
            while (Marshal.ReadByte(pointer, len) != 0) { ++len; }
            byte[] buffer = new byte[len];
            Marshal.Copy(pointer, buffer, 0, buffer.Length);
            return Encoding.UTF8.GetString(buffer);
        }

        internal class StringAsReturnValue : SafeHandle
        {
            public StringAsReturnValue() : base(IntPtr.Zero, true) { }

            public override bool IsInvalid
            {
                get { return this.handle == IntPtr.Zero; }
            }

            public string AsString()
            {
                return GetFromRustString(handle);
            }

            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    Console.WriteLine("Freeing string handle");
                    glean_str_free(handle);
                }

                return true;
            }
        }

        // Glean top-level API.

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_initialize(FfiConfiguration cfg);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_clear_application_lifetime_metrics();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_set_dirty_flag(byte flag);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_is_dirty_flag_set();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_test_clear_all_stores();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_is_first_run();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_destroy_glean();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_on_ready_to_submit_pings();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_enable_logging();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_set_upload_enabled(bool flag);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_is_upload_enabled();

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern StringAsReturnValue glean_ping_collect(PingTypeHandle ping_type_handle, string reason);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_submit_ping_by_name(string ping_name, string reason);

        // TODO: add the rest of the ffi.

        // String

        /// <summary>
        /// A handle for the string metric type, which performs cleanup.
        /// </summary>
        internal sealed class StringMetricTypeHandle : BaseGleanHandle
        {
            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    glean_destroy_string_metric(handle);
                }

                return true;
            }
        }

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern StringMetricTypeHandle glean_new_string_metric(
            string category,
            string name,
            string[] send_in_pings,
            Int32 send_in_pings_len,
            Int32 lifetime,
            bool disabled
        );

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_destroy_string_metric(IntPtr handle);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_string_set(StringMetricTypeHandle metric_id, string value);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern StringAsReturnValue glean_string_test_get_value(StringMetricTypeHandle metric_id, string storage_name);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_string_test_has_value(StringMetricTypeHandle metric_id, string storage_name);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern Int32 glean_string_test_get_num_recorded_errors(
             StringMetricTypeHandle metric_id,
             Int32 error_type,
             string storage_name
        );

        // Boolean

        /// <summary>
        /// A handle for the boolean metric type, which performs cleanup.
        /// </summary>
        internal sealed class BooleanMetricTypeHandle : BaseGleanHandle
        {
            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    glean_destroy_boolean_metric(handle);
                }

                return true;
            }
        }

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern BooleanMetricTypeHandle glean_new_boolean_metric(
            string category,
            string name,
            string[] send_in_pings,
            Int32 send_in_pings_len,
            Int32 lifetime,
            bool disabled
        );

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_destroy_boolean_metric(IntPtr handle);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_boolean_set(BooleanMetricTypeHandle metric_id, byte value);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_boolean_test_get_value(BooleanMetricTypeHandle metric_id, string storage_name);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_boolean_test_has_value(BooleanMetricTypeHandle metric_id, string storage_name);

        // Uuid

        /// <summary>
        /// A handle for the uuid metric type, which performs cleanup.
        /// </summary>
        internal sealed class UuidMetricTypeHandle : BaseGleanHandle
        {
            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    glean_destroy_uuid_metric(handle);
                }

                return true;
            }
        }

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern UuidMetricTypeHandle glean_new_uuid_metric(
            string category,
            string name,
            string[] send_in_pings,
            Int32 send_in_pings_len,
            Int32 lifetime,
            bool disabled
        );

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_destroy_uuid_metric(IntPtr handle);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_uuid_set(UuidMetricTypeHandle metric_id, string value);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern StringAsReturnValue glean_uuid_test_get_value(UuidMetricTypeHandle metric_id, string storage_name);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_uuid_test_has_value(UuidMetricTypeHandle metric_id, string storage_name);

        // Custom pings

        /// <summary>
        /// A handle for the ping metric type, which performs cleanup.
        /// </summary>
        internal sealed class PingTypeHandle : BaseGleanHandle
        {
            protected override bool ReleaseHandle()
            {
                if (!this.IsInvalid)
                {
                    glean_destroy_ping_type(handle);
                }

                return true;
            }
        }

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern PingTypeHandle glean_new_ping_type(
            string name,
            byte include_client_id,
            byte send_if_empty,
            string[] reason,
            Int32 reason_codes_len
        );

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_destroy_ping_type(IntPtr handle);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_register_ping_type(PingTypeHandle ping_type_handle);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern byte glean_test_has_ping_type(string ping_name);

        // Upload API

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_get_upload_task(ref FfiUploadTask result, bool logPing);

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_process_ping_upload_response(IntPtr task, int status);

        // Misc

        [DllImport(SharedGleanLibrary, ExactSpelling = true, CallingConvention = CallingConvention.Cdecl)]
        internal static extern void glean_str_free(IntPtr ptr);
    }
}
