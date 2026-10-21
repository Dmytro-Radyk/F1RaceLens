namespace F1RaceLens.Api.Accounts;

public static class AccountMailText
{
    public static (string Subject, string Body) Create(AccountMailKind kind, string language, string link)
    {
        var titles = language switch {
            "uk" => new[] { "Відновлення пароля", "Підтвердження пошти", "Підтвердження нової пошти" },
            "de" => ["Passwort zurücksetzen", "E-Mail bestätigen", "Neue E-Mail bestätigen"],
            "it" => ["Reimposta password", "Conferma email", "Conferma nuova email"],
            "es" => ["Restablecer contraseña", "Confirmar correo", "Confirmar nuevo correo"],
            "fr" => ["Réinitialiser le mot de passe", "Confirmer l’adresse email", "Confirmer la nouvelle adresse email"],
            _ => ["Password reset", "Email confirmation", "Confirm new email"]
        };
        var instruction = language switch {
            "uk" => "Відкрийте одноразове посилання протягом 30 хвилин. Якщо ви не надсилали цей запит, проігноруйте лист. Без підтвердження нічого не зміниться.",
            "de" => "Öffnen Sie den einmaligen Link innerhalb von 30 Minuten. Falls Sie dies nicht angefordert haben, ignorieren Sie diese Nachricht. Ohne Bestätigung wird nichts geändert.",
            "it" => "Apri il link monouso entro 30 minuti. Se non hai richiesto questa operazione, ignora il messaggio. Senza conferma non cambierà nulla.",
            "es" => "Abre el enlace de un solo uso en un plazo de 30 minutos. Si no lo solicitaste, ignora este mensaje. Sin confirmación no cambiará nada.",
            "fr" => "Ouvrez le lien à usage unique dans les 30 minutes. Si vous n’avez pas fait cette demande, ignorez ce message. Rien ne changera sans confirmation.",
            _ => "Open this single-use link within 30 minutes. If you did not request this, ignore this email. Nothing changes without confirmation."
        };
        return ("F1 Race Lens — " + titles[(int)kind], instruction + "\n\n" + link);
    }
}
