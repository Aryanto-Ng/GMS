using System.Text.Json.Serialization;

namespace Gms.Shared.Enums;

// Semua enum diserialisasi sebagai string PascalCase (ApiConventions.md) dan disimpan sebagai text di database.

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActiveFilter { Active, Inactive, All }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum UserStatus { Active, Inactive, Locked }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProcessType { Internal, Vendor }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PoStatus { Draft, Confirmed, InProgress, Completed, Cancelled }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WoStatus { Draft, Released, InProgress, Completed, Closed, Cancelled }

/// <summary>Status dokumen ledger/dokumen transaksi (void tidak menghapus data).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DocStatus { Active, Voided }

/// <summary>Jenis entri output pekerja.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OutputKind { Output, Adjustment }

/// <summary>Jenis baris pada riwayat produksi gabungan.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EntryKind { Output, Adjustment, VendorSend, VendorReceive }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum VendorMovementKind { Send, Receive }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AllocationKind { Allocate, Release, Consume, ReverseConsume }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AllocationSource { Manual, Handover, HandoverVoid, PoCancel, Delivery, DeliveryVoid }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StockLedgerKind { HandoverReceipt, HandoverReversal, Delivery, DeliveryReversal, Adjustment, OpeningBalance }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayrollStatus { Open, Closed }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StepState { NotStarted, InProgress, Done }

/// <summary>Alasan sesi dicabut (disimpan di user_sessions.revoked_reason).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SessionRevokedReason { Logout, Timeout, Replaced, AdminForced, Deactivated, PasswordReset }

/// <summary>Entitas yang mendukung impor CSV.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ImportEntity { Material, Fg, Worker, StockOpening }

/// <summary>Format ekspor periode gaji.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PayrollExportFormat { Xlsx, CsvSummary, CsvDaily }
