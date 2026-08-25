namespace GvrTools.Licensing.Crypto
{
    /// <summary>
    /// Clave pública ECDsa P-256 del License API, en formato SubjectPublicKeyInfo/base64. Se rota
    /// junto con un update firmado (docs/LICENSING_PLAN.md, "Tokens y gracia offline").
    ///
    /// NOTA: el valor de abajo es de un par de claves de DESARROLLO generado con
    /// server/tools/GenerateSigningKey para poder construir y probar este verificador. Antes de
    /// vender la primera licencia hay que generar el par de producción y reemplazar este valor (la
    /// privada correspondiente va SOLO en Signing__PrivateKeyPem de EasyPanel, nunca en git).
    /// </summary>
    public static class EmbeddedPublicKey
    {
        public const string Base64 =
            "So0DjYjTSlZ3yxlwmgkTj5YA+7MKPMotbrUyFPCX8yQyfnT9hIT8ODkOBUJ9656NBnDtfv4pNOfOq16ySesQZw==";
    }
}
