using System.ComponentModel.DataAnnotations;

namespace MesApp.Core.Contracts.Setup;

/// <summary>初期セットアップ状態（Spec.md 2.2 E：Userが0件のときのみセットアップ可能）</summary>
public record SetupStatusResponse(bool SetupRequired);

public record InitializeRequest(
    [Required] string UserName,
    [Required] string Password,
    [Required] string DisplayName);
