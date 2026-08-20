$projectRoot = Split-Path -Parent $PSScriptRoot
$publishFolder = Join-Path $projectRoot 'artifacts\RutaCashflow-win-x64'
$executablePath = Join-Path $publishFolder 'RutaCashflow.exe'

if (-not (Test-Path -LiteralPath $executablePath)) {
    & (Join-Path $PSScriptRoot 'Publish-Windows.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw 'No se pudo publicar RutaCashflow antes de crear el acceso directo.'
    }
}

$documentsFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::MyDocuments)
$codexAppsFolder = Join-Path $documentsFolder 'Codex\CODEX APPS'
$shortcutPath = Join-Path $codexAppsFolder 'Calculadora.lnk'

[System.IO.Directory]::CreateDirectory($codexAppsFolder) | Out-Null

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $executablePath
$shortcut.WorkingDirectory = $projectRoot
$shortcut.IconLocation = "$executablePath,0"
$shortcut.Description = 'Calculadora local de rutas de transferencia, comisiones y conversiones'
$shortcut.WindowStyle = 1
$shortcut.Save()

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace RutaCashflowShortcut
{
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;

        public PropertyKey(Guid formatId, uint propertyId)
        {
            FormatId = formatId;
            PropertyId = propertyId;
        }
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort VariantType;
        [FieldOffset(8)] public IntPtr PointerValue;

        public static PropVariant FromString(string value)
        {
            return new PropVariant
            {
                VariantType = 31,
                PointerValue = Marshal.StringToCoTaskMemUni(value)
            };
        }

        public string GetString()
        {
            return VariantType == 31 && PointerValue != IntPtr.Zero
                ? Marshal.PtrToStringUni(PointerValue)
                : null;
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    internal class ShellLink
    {
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }

    public static class ShortcutIdentity
    {
        private static PropertyKey AppUserModelIdKey = new PropertyKey(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            5);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        public static void Set(string shortcutPath, string appUserModelId)
        {
            object shellLink = new ShellLink();
            try
            {
                IPersistFile file = (IPersistFile)shellLink;
                file.Load(shortcutPath, 2);
                IPropertyStore store = (IPropertyStore)shellLink;
                PropVariant value = PropVariant.FromString(appUserModelId);
                try
                {
                    Marshal.ThrowExceptionForHR(store.SetValue(ref AppUserModelIdKey, ref value));
                    Marshal.ThrowExceptionForHR(store.Commit());
                    file.Save(shortcutPath, true);
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }

        public static string Get(string shortcutPath)
        {
            object shellLink = new ShellLink();
            try
            {
                ((IPersistFile)shellLink).Load(shortcutPath, 0);
                IPropertyStore store = (IPropertyStore)shellLink;
                PropVariant value;
                Marshal.ThrowExceptionForHR(store.GetValue(ref AppUserModelIdKey, out value));
                try
                {
                    return value.GetString();
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }
}
'@

$appUserModelId = 'Local.Calculadora.Desktop'
[RutaCashflowShortcut.ShortcutIdentity]::Set($shortcutPath, $appUserModelId)
$savedAppUserModelId = [RutaCashflowShortcut.ShortcutIdentity]::Get($shortcutPath)
if ($savedAppUserModelId -ne $appUserModelId) {
    throw "El acceso directo no conservo el AppUserModelID esperado: $appUserModelId"
}

Write-Host "Acceso directo creado en: $shortcutPath"
Write-Host "AppUserModelID: $savedAppUserModelId"
