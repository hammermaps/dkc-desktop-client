<?php
/**
 * MIT License
 *
 * Copyright (c) 2023 Lucas Eulberg
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 */

const _ROOT_ = __DIR__;

// Umgebungskonfiguration laden (.env-Datei)
require_once _ROOT_ . '/config/env.php';

define("DEBUG", filter_var(getenv('APP_DEBUG') ?: 'false', FILTER_VALIDATE_BOOLEAN));
if(DEBUG) {
    error_reporting(E_ALL);
    ini_set('display_errors', 1);
    ini_set('display_startup_errors', 1);
    ini_set('log_errors', 1);
    ini_set('error_log', _ROOT_ . '/logs/PHP-Error-DEV.log');
    //Deaktivire opcache im Debug-Modus
    ini_set('opcache.enable', '0');
    ini_set('opcache.enable_cli', '0');
    ini_set('max_execution_time', '90'); //PHP Ausführungzeit erhöhen
} else {
    error_reporting(E_ERROR | E_WARNING | E_PARSE | E_NOTICE);
    ini_set('display_errors', 0);
    ini_set('log_errors', 1);
    ini_set('error_log', _ROOT_ . '/logs/PHP-Error-Runtime.log');
}

date_default_timezone_set(getenv('APP_TIMEZONE') ?: 'Europe/Berlin');

require_once __DIR__.'/vendor/autoload.php';

use JetBrains\PhpStorm\NoReturn;
use Monolog\Handler\StreamHandler;
use Monolog\Level;
use Monolog\Logger;
use Nette\Database\Table\ActiveRow;
use Nette\Utils\DateTime;
use system\helper\AppContainer;
use system\helper\CacheHelper;
use system\helper\CompressionHelper;
use system\helper\Database;
use system\helper\GotifyHelper;
use system\helper\MailHelper;
use system\helper\RemoteAddress;
use system\helper\RMI;
use system\helper\ExternalServices;
use system\helper\ProjectContext;
use system\helper\SecurityHardening;
use system\helper\UnifiMailHelper;
use Smarty\Smarty;
use system\User;

// AppContainer initialisieren – registriert gemeinsame Dienste (Flysystem, MimeTypeDetector, Database, Cache)
$appContainer = AppContainer::getInstance();

// Sicherheits-Härtung
$logger = new Monolog\Logger('Security');
$logger->pushHandler(new Monolog\Handler\StreamHandler(_ROOT_.'/logs/security.log', DEBUG ? Monolog\Level::Debug : Monolog\Level::Warning));
$securityHardening = SecurityHardening::getInstance($logger);
$securityHardening->hardenPHPEnvironment();
$securityHardening->configureSecureSessions();

const API_LOG_MAX_VALUE_LENGTH = 4096;
// Fallback detector for simple application/x-www-form-urlencoded bodies when Content-Type is missing.
// Matches RFC 3986-style key/value fields using unreserved or percent-encoded key characters,
// joined by "&", and requires at least one "=" or "&" separator to avoid treating plain text as a form.
const API_FORM_URLENCODED_PATTERN = '/^(?=.*[=&])(?:[A-Za-z0-9_.~%+-]+(?:=[^&]*)?)(?:&[A-Za-z0-9_.~%+-]+(?:=[^&]*)?)*$/';
const API_FORM_MAX_FIELDS = 200;

function sanitizeApiLogData(mixed $value): mixed
{
    if (is_array($value)) {
        $sanitized = [];
        foreach ($value as $key => $item) {
            $keyString = (string)$key;
            if (preg_match('/(?:api[_-]?(?:key|secret)|token|secret|password|passwd|pwd|auth(?:orization)?|bearer|cookie|credential|session|csrf|nonce)/i', $keyString)) {
                $sanitized[$key] = '[redacted]';
                continue;
            }
            $sanitized[$key] = sanitizeApiLogData($item);
        }
        return $sanitized;
    }

    if (is_string($value) && strlen($value) > API_LOG_MAX_VALUE_LENGTH) {
        return substr($value, 0, API_LOG_MAX_VALUE_LENGTH) . '…[truncated]';
    }

    return $value;
}

function parseApiFormLogBody(string $body, int $maxFields): ?array
{
    $parsed = [];
    $offset = 0;
    $fieldCount = 0;

    while (true) {
        if ($fieldCount >= $maxFields) {
            return null;
        }

        $separatorPos = strpos($body, '&', $offset);
        $field = $separatorPos === false
            ? substr($body, $offset)
            : substr($body, $offset, $separatorPos - $offset);
        $fieldCount++;

        if ($field === '') {
            if ($separatorPos === false) {
                break;
            }
            $offset = $separatorPos + 1;
            continue;
        }

        $parts = explode('=', $field, 2);
        $key = urldecode($parts[0]);
        if ($key === '') {
            continue;
        }

        $parsed[$key] = urldecode($parts[1] ?? '');

        if ($separatorPos === false) {
            break;
        }
        $offset = $separatorPos + 1;
    }

    return $parsed;
}

function sanitizeApiLogBody(string $body, ?string $contentType = null): ?string
{
    if ($body === '') {
        return null;
    }

    $decoded = json_decode($body, true);
    if (is_array($decoded)) {
        return json_encode(sanitizeApiLogData($decoded), JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES);
    }

    $isFormUrlencoded = $contentType !== null
        && stripos($contentType, 'application/x-www-form-urlencoded') !== false;
    $isBodyWithinFormLogLimit = strlen($body) <= API_LOG_MAX_VALUE_LENGTH;
    $shouldUseFallbackFormParsing = !$isFormUrlencoded
        && $isBodyWithinFormLogLimit
        && preg_match(API_FORM_URLENCODED_PATTERN, $body);
    if ($isFormUrlencoded || $shouldUseFallbackFormParsing) {
        if (!$isBodyWithinFormLogLimit) {
            return '[form body omitted: too large]';
        }
        $parsedBody = parseApiFormLogBody($body, API_FORM_MAX_FIELDS);
        if ($parsedBody === null) {
            return '[form body omitted: too many fields]';
        }
        if (is_array($parsedBody) && $parsedBody !== []) {
            return http_build_query(sanitizeApiLogData($parsedBody), '', '&', PHP_QUERY_RFC3986);
        }
    }

    return strlen($body) > API_LOG_MAX_VALUE_LENGTH ? substr($body, 0, API_LOG_MAX_VALUE_LENGTH) . '…[truncated]' : $body;
}

// Globale Eingabe-Prüfung auf XSS – Anfrage sofort abbrechen wenn Angriff erkannt
foreach ([$_GET, $_POST] as $requestData) {
    foreach ($requestData as $key => $value) {
        if (is_string($value)) {
            if ($securityHardening->detectXSS($value)) {
                http_response_code(400);
                header('Content-Type: application/json; charset=UTF-8');
                echo json_encode(['error' => 'Invalid request']);
                exit();
            }
        } elseif (is_array($value)) {
            $xssFound = false;
            array_walk_recursive($value, function($item) use ($securityHardening, &$xssFound) {
                if (!$xssFound && is_string($item) && $securityHardening->detectXSS($item)) {
                    $xssFound = true;
                }
            });
            if ($xssFound) {
                http_response_code(400);
                header('Content-Type: application/json; charset=UTF-8');
                echo json_encode(['error' => 'Invalid request']);
                exit();
            }
        }
    }
}

// CacheHelper initialisieren
$cache = CacheHelper::getInstance();

// create a log channel
$log = new Logger('API');
$log->pushHandler(new StreamHandler(_ROOT_.'/logs/API.log', Level::Error));

// DkcDesktop-spezifischer Logger – greift nur bei User-Agent: DkcDesktopClient/x.x.x
$dkcLog = new Logger('DkcDesktop');
$dkcLog->pushHandler(new StreamHandler(_ROOT_.'/logs/DkcDesktop.log', Level::Debug));

// Zeitmessung für Gesamtduration
$requestStartTime = microtime(true);

// Starte intelligente Komprimierung (zstd, br, gzip, deflate)
CompressionHelper::start(DEBUG);

// Request-Einstiegslog
$log->info('API request', [
    'method'     => $_SERVER['REQUEST_METHOD'] ?? 'GET',
    'uri'        => $_SERVER['REQUEST_URI']    ?? '',
    'action'     => strtolower(trim($_GET['action'] ?? 'sync')),
    'ip'         => (new RemoteAddress())->getClientIP(),
    'user_agent' => $_SERVER['HTTP_USER_AGENT'] ?? 'unknown',
]);

// DkcDesktop-Logging: vollständige Request-Aufzeichnung bei passendem User-Agent
if (preg_match('/^DkcDesktopClient\//i', $_SERVER['HTTP_USER_AGENT'] ?? '')) {
    $dkcRequestBody = file_get_contents('php://input');
    $dkcPostData    = sanitizeApiLogData($_POST);
    $dkcGetData     = sanitizeApiLogData($_GET);

    // Relevante HTTP-Headers sammeln
    $dkcHeaders = [];
    foreach ($_SERVER as $k => $v) {
        if (str_starts_with($k, 'HTTP_') && $k !== 'HTTP_AUTHORIZATION') {
            $dkcHeaders[substr($k, 5)] = $v;
        }
    }

    $dkcLog->info('REQUEST', [
        'method'      => $_SERVER['REQUEST_METHOD'] ?? 'GET',
        'uri'         => $_SERVER['REQUEST_URI']    ?? '',
        'path_info'   => $_SERVER['PATH_INFO']      ?? '',
        'action'      => strtolower(trim($_GET['action'] ?? '')),
        'ip'          => (new RemoteAddress())->getClientIP(),
        'user_agent'  => $_SERVER['HTTP_USER_AGENT'] ?? '',
        'get_params'  => $dkcGetData,
        'post_params' => $dkcPostData,
        'body'        => sanitizeApiLogBody($dkcRequestBody, $_SERVER['CONTENT_TYPE'] ?? $_SERVER['HTTP_CONTENT_TYPE'] ?? null),
        'headers'     => $dkcHeaders,
    ]);
    unset($dkcRequestBody, $dkcPostData, $dkcGetData, $dkcHeaders);
}

try {
    $database = Database::getInstance();
} catch (Exception $e) {
    if (DEBUG) {
        error_log('API Datenbankfehler: ' . $e->getMessage());
    }
    http_response_code(503);
    header('Content-Type: application/json; charset=UTF-8');
    echo json_encode(['error' => 'Service temporarily unavailable']);
    exit();
}

// Lazy Loading für Smarty
function getSmarty(): Smarty {
    static $smarty = null;
    if ($smarty === null) {
        $cache = CacheHelper::getInstance();
        // Ensure directories exist
        if(!file_exists(_ROOT_.'/cache/tpl'))
            if (!mkdir($concurrentDirectory = _ROOT_ . '/cache/tpl', 0777, true) && !is_dir($concurrentDirectory)) {
                throw new \RuntimeException(sprintf('Directory "%s" was not created', $concurrentDirectory));
            }
        if(!file_exists(_ROOT_.'/compile/default'))
            if (!mkdir($concurrentDirectory = _ROOT_ . '/compile/default', 0777, true) && !is_dir($concurrentDirectory)) {
                throw new \RuntimeException(sprintf('Directory "%s" was not created', $concurrentDirectory));
            }

        $smarty = new Smarty();
        $smarty->setTemplateDir(_ROOT_.'/template/default');
        $smarty->setCompileDir(_ROOT_.'/compile/default');
        $smarty->setConfigDir(_ROOT_.'/config');
        $smarty->setCacheDir(_ROOT_.'/cache/tpl');

        // Registriere benutzerdefinierte Smarty-Modifier automatisch
        $cacheKey = 'smarty_modifiers_list';

        // Versuche aus Cache zu laden
        $cachedModifiers = $cache->get($cacheKey);
        if ($cachedModifiers !== null && !DEBUG) {
            foreach ($cachedModifiers as $modifierFile => $modifierName) {
                require_once $modifierFile;
                $functionName = 'smarty_modifier_' . $modifierName;
                if (function_exists($functionName)) {
                    try {
                        $smarty->registerPlugin('modifier', $modifierName, $functionName);
                    } catch (\Smarty\Exception $e) {
                        if (DEBUG) {
                            error_log("Fehler beim Registrieren des Modifiers '$modifierName': " . $e->getMessage());
                        }
                    }
                }
            }
        } else {
            // Cache nicht vorhanden oder Debug-Modus, neu laden
            $modifierPath = _ROOT_ . '/smarty_plugins/modifier.*.php';
            $modifierFiles = glob($modifierPath);
            $modifierCache = [];
            foreach ($modifierFiles as $modifierFile) {
                $filename = basename($modifierFile);
                if (preg_match('/^modifier\.(.+)\.php$/', $filename, $matches)) {
                    $modifierName = $matches[1];
                    $functionName = 'smarty_modifier_' . $modifierName;
                    require_once $modifierFile;
                    if (function_exists($functionName)) {
                        try {
                            $smarty->registerPlugin('modifier', $modifierName, $functionName);
                        } catch (\Smarty\Exception $e) {
                            if (DEBUG) {
                                error_log("Fehler beim Registrieren des Modifiers '$modifierName': " . $e->getMessage());
                            }
                        }
                        $modifierCache[$modifierFile] = $modifierName;
                    } elseif (DEBUG) {
                        error_log("Warnung: Funktion '$functionName' nicht gefunden in '$modifierFile'");
                    }
                }
            }

            // In Cache speichern (24 Stunden), falls nicht im Debug-Modus
            if (!DEBUG) {
                $cache->set($cacheKey, $modifierCache, 86400, [CacheHelper::TAG_NAVIGATION]);
            }
        }

        $smarty->assign('assets', 'template/default/assets');
        $smarty->setForceCompile(DEBUG);
        $smarty->setDebugging(DEBUG);
    }
    return $smarty;
}

// Caching der IP-Whitelist
function loadIPWhitelist(): array {
    static $cache = null;
    if ($cache !== null) return $cache;

    if (!file_exists(_ROOT_.'/config/ips.txt')) {
        return $cache = [];
    }

    $lines = file(_ROOT_.'/config/ips.txt', FILE_IGNORE_NEW_LINES | FILE_SKIP_EMPTY_LINES);
    $cache = array_filter($lines, fn($line) => !str_starts_with(trim($line), '#'));
    return $cache;
}

// CIDR-Prüfung auslagern
function isIPInRange(string $ip, string $range): bool {
    if ($ip === $range) return true;

    if (!str_contains($range, '/')) return false;

    [$subnet, $mask] = explode('/', $range);
    $mask = (int)$mask;

    if ($mask < 0 || $mask > 32) return false;

    $ipLong = ip2long($ip);
    $subnetLong = ip2long($subnet);

    if ($ipLong === false || $subnetLong === false) return false;

    $maskLong = $mask === 0 ? 0 : (-1 << (32 - $mask));
    return ($ipLong & $maskLong) === ($subnetLong & $maskLong);
}

// Response-Helper
function jsonResponse(array $data, int $code = 200): never {
    global $dkcLog, $requestStartTime;

    // DkcDesktop Response-Logging
    if (preg_match('/^DkcDesktopClient\//i', $_SERVER['HTTP_USER_AGENT'] ?? '')) {
        $duration = round((microtime(true) - ($requestStartTime ?? microtime(true))) * 1000, 2);

        // Bei großen Datensätzen nur Metadaten loggen (kein Memory-Overhead)
        $responsePreview = $data;
        array_walk_recursive($responsePreview, static function(&$val) {
            if (is_string($val) && strlen($val) > 500) {
                $val = substr($val, 0, 500) . '…[truncated]';
            }
        });

        $logMethod = $code >= 500 ? 'error' : ($code >= 400 ? 'warning' : 'info');
        $dkcLog->$logMethod('RESPONSE', [
            'http_code'   => $code,
            'duration_ms' => $duration,
            'action'      => strtolower(trim($_GET['action'] ?? '')),
            'uri'         => $_SERVER['REQUEST_URI'] ?? '',
            'response'    => $responsePreview,
        ]);
    }

    http_response_code($code);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($data, JSON_UNESCAPED_UNICODE);
    exit;
}

function errorResponse(string $message, int $code = 400): never {
    jsonResponse(['error' => $message], $code);
}

// Exception Handler
set_exception_handler(static function(Throwable $e) use ($log) {
    $log->error('Unhandled exception', [
        'message' => $e->getMessage(),
        'file' => $e->getFile(),
        'line' => $e->getLine()
    ]);
    http_response_code(500);
    echo DEBUG ? $e->getMessage() : 'Internal Server Error';
    exit;
});

$radd = new RemoteAddress();

// ============================================================================
// TWS-App REST API – PATH_INFO-basiertes Routing
// Endpunkte: /user/*, /buildings/*, /apartments/*, /records/*, /health
// Auth: Authorization: Bearer dkc_... (user_api_tokens) oder aktive Session
// Aufruf z. B.: https://rmi.dk-automation.de/api.php/user/login
// ============================================================================
{
    $twsPath = $_SERVER['PATH_INFO'] ?? '';
    if ($twsPath === '') {
        $uri        = parse_url($_SERVER['REQUEST_URI'] ?? '', PHP_URL_PATH) ?? '';
        $scriptName = $_SERVER['SCRIPT_NAME'] ?? '/api.php';
        if ($scriptName !== '' && str_starts_with($uri, $scriptName) && strlen($uri) > strlen($scriptName)) {
            $twsPath = substr($uri, strlen($scriptName));
        }
    }
    if ($twsPath !== '' && $twsPath !== '/') {
        handleTwsRequest($twsPath, $database, $log, $radd);
        // handleTwsRequest always exits; this line is never reached
    }
}

$api_functions = [
    'sync' => false,
    'sms' => false,
    'email' => false,
    'rmi' => false,
    'gotify' => false,
    'webhook' => false,
];

$api_data = [];

//Prüfe ob ein API-Key gesetzt ist POST['apikey'] oder im Authorization Header (Bearer Token)
$current_api_key = array_key_exists('apikey', $_POST) ? trim($_POST['apikey']) : (
(static function() {
    $headers = getallheaders();
    if (isset($headers['Authorization'])) {
        if (preg_match('/^Bearer\s+(.+)$/i', $headers['Authorization'], $matches)) {
            return trim($matches[1]);
        }
    }
    return null;
})()
);

//Suche anhand der API Key die eindeutige ID für Logging und weitere Prüfungen
$current_api_key_id = null; // Wird gesetzt wenn ein API Key verwendet wird
$_earlyAction = strtolower(trim($_GET['action'] ?? 'sync'));
$_earlyPublic = ['auth_login']; // Public Actions benötigen keinen API Key
if($current_api_key != null && !in_array($_earlyAction, $_earlyPublic, true)) {
    $api = $database->getExplorer()->table('api')->where('key', $current_api_key)->fetch();
    if($api && $api->offsetGet('enabled')) {
        $current_api_key_id = $api->offsetGet('id');
    } else {
        // Könnte ein User-Token (dkc_...) sein – kein Fehler für userTokenActions
        $_earlyUserToken = ['auth_logout', 'auth_status', 'user_info',
            'nea_systems', 'nea_inspections', 'nea_inspection_detail', 'nea_dashboard',
            'mm_list', 'mm_detail', 'building_list', 'building_inspections', 'building_inspection_detail',
            'klima_devices', 'klima_status', 'keys_inventory', 'keys_issued',
            'dashboard_data', 'projects_list', 'user_tokens_list', 'user_token_delete'];
        if (!in_array($_earlyAction, $_earlyUserToken, true)) {
            $log->warning("Invalid or inactive API Key from IP: " . $radd->getClientIP(),
                ['post' => $_POST, 'get' => $_GET, 'headers' => getallheaders()]
            );
            errorResponse('Invalid or inactive API Key', 401);
        }
        // User-Token-Actions: API-Key-Check überspringen, wird später geprüft
    }
}

// Actions die KEINEN API-Key benötigen (session-basiert, klassisch)
$sessionBasedActions = [
    'notifications', 'get_notification_count', 'ckeditor_draft', 'client_cache_version',
    // Zählererfassung PWA
    'meter_list', 'meter_submit', 'meter_batch_sync', 'meter_readings',
    'meter_qr_list', 'meter_deactivate', 'meter_activate',
    'meter_buildings', 'meter_whg', 'meter_users',
    'meter_topology',
    'dropdown_data',
];

// Öffentliche Actions – kein Auth erforderlich (z. B. Login)
$publicActions = ['auth_login'];

// Benutzer-Token-basierte Actions (Session ODER persönlicher User-API-Token dkc_...)
$userTokenActions = [
    'auth_logout', 'auth_status', 'user_info',
    // NEA (Netzersatzanlagen)
    'nea_systems', 'nea_inspections', 'nea_inspection_detail', 'nea_dashboard',
    // MM (Mängelmeldungen)
    'mm_list', 'mm_detail',
    // Gebäudebegehungen
    'building_list', 'building_inspections', 'building_inspection_detail',
    // Klima (Klimaanlage / Air Conditioner)
    'klima_devices', 'klima_status',
    // Schlüsselverwaltung
    'keys_inventory', 'keys_issued',
    // Dashboard & Projekte
    'dashboard_data', 'projects_list',
    // Benutzer-API-Token-Verwaltung
    'user_tokens_list', 'user_token_delete',
];

$currentAction = strtolower(trim($_GET['action'] ?? 'sync'));
$requiresApiKey = !in_array($currentAction, array_merge($sessionBasedActions, $publicActions, $userTokenActions), true);

// Auth-Prüfung für nicht-API-Key-Actions
if (in_array($currentAction, $publicActions, true)) {
    // Öffentliche Action – kein Auth-Check (z. B. auth_login)
} elseif (!$requiresApiKey) {
    if (!empty($_SESSION['id'])) {
        // Aktive Browser-Session vorhanden
        $log->debug('API action with valid session', [
            'action' => $currentAction,
            'ip' => $radd->getClientIP()
        ]);
    } elseif (in_array($currentAction, $userTokenActions, true)) {
        // Kein Session – prüfe persönlichen User-API-Token (Authorization: Bearer dkc_...)
        $utCandidate = null;
        $utHeaders = getallheaders();
        if (isset($utHeaders['Authorization']) && preg_match('/^Bearer\s+(dkc_\S+)$/i', $utHeaders['Authorization'], $utm)) {
            $utCandidate = trim($utm[1]);
        }
        if ($utCandidate !== null) {
            $utRow = $database->getExplorer()->table('user_api_tokens')
                ->where('token', hash('sha256', $utCandidate))
                ->where('expires_at IS NULL OR expires_at > NOW()')
                ->fetch();
            if ($utRow) {
                // Token gültig – User-ID setzen und Projekt-Kontext initialisieren
                $_SESSION['id'] = $utRow->offsetGet('user_id');
                ProjectContext::getInstance($database)->initForUser((int)$_SESSION['id']);
                try {
                    $database->getExplorer()->table('user_api_tokens')
                        ->where('id', $utRow->offsetGet('id'))
                        ->update(['last_used_at' => new DateTime(), 'last_ip' => $radd->getClientIP()]);
                } catch (Exception $e) {
                    $log->warning('user_api_tokens update failed: ' . $e->getMessage());
                }
                $log->debug('User-API-Token validated', ['user_id' => $_SESSION['id'], 'action' => $currentAction]);
            } else {
                $log->warning('Invalid or expired User-API-Token', ['action' => $currentAction, 'ip' => $radd->getClientIP()]);
                jsonResponse(['success' => false, 'error' => 'Invalid or expired user token'], 401);
            }
        } else {
            jsonResponse(['success' => false, 'error' => 'Authentication required. Provide a session cookie or Authorization: Bearer dkc_... token.'], 401);
        }
    } else {
        // Klassische session-basierte Action ohne Session – leere Antwort
        $log->debug('Session-based API action without valid session - returning empty response', [
            'action' => $currentAction,
            'ip' => $radd->getClientIP()
        ]);
        jsonResponse(['authenticated' => false, 'count' => 0, 'notifications' => [], 'version' => null], 401);
    }
}

// IP-Filter && API Key check
if(file_exists(_ROOT_.'/config/ips.txt') && $requiresApiKey) {
    $clientIP = $radd->getClientIP();
    $ipInWhitelist = false;

    // Parse global whitelist (supports CIDR and comments)
    if($radd->isOnline()) {
        $whitelist = loadIPWhitelist();
        foreach($whitelist as $line) {
            if(isIPInRange($clientIP, trim($line))) {
                $ipInWhitelist = true;
                break;
            }
        }
    }

    if(!$ipInWhitelist && $radd->isOnline()) {
        // API-Key aus POST-Parameter oder Authorization Bearer Header
        $apiKey = $_POST['apikey'] ?? '';

        // Prüfe Authorization Header (Bearer Token)
        if(empty($apiKey)) {
            $headers = getallheaders();
            if(isset($headers['Authorization'])) {
                // Format: "Bearer xxxx-xxxx-xxxx-xxxx"
                if(preg_match('/^Bearer\s+(.+)$/i', $headers['Authorization'], $matches)) {
                    $apiKey = trim($matches[1]);
                }
            }
        }

        //Check is API Key a UID V4 format (24c71e58-da22-4b28-9600-7bf377cdd44f)
        if (!preg_match('/^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/', $apiKey)) {
            $log->warning("Invalid or inactive API Key from IP: " . $radd->getClientIP(),
                ['post' => $_POST,
                    'get' => $_GET,
                    'headers' => getallheaders()
                ]
            );
            errorResponse('Invalid API Key format', 401);
        }

        $api = $database->getExplorer()->table('api')->where('key', $apiKey)->fetch();
        if(!$api || !$api->offsetGet('enabled')) {
            $log->warning("Invalid or inactive API Key from IP: " . $radd->getClientIP(),
                ['post' => $_POST,
                    'get' => $_GET,
                    'headers' => getallheaders()
                ]
            );
            errorResponse('Invalid or inactive API Key', 401);
        }

        // Setze globale Variablen für Logging
        $current_api_key_id = $api->offsetGet('id');
        $current_api_key = $apiKey;



        //Check IP Whitelist for this API Key
        try {
            $apiData = json_decode($api->offsetGet('data'), true, 512, JSON_THROW_ON_ERROR) ?? [];
        } catch (JsonException $e) {
            $log->error("Failed to decode API data for API Key: " . $apiKey . " Error: " . $e->getMessage());
            $apiData = [];
        }

        if(isset($apiData['allowed_ips']) && is_array($apiData['allowed_ips']) && !empty($apiData['allowed_ips'])) {
            $clientIP = $radd->getClientIP();
            $ipAllowed = false;

            foreach($apiData['allowed_ips'] as $allowedIP) {
                if(isIPInRange($clientIP, trim($allowedIP))) {
                    $ipAllowed = true;
                    break;
                }
            }

            if(!$ipAllowed) {
                $log->warning("IP not in whitelist for API Key from IP: " . $clientIP, ['apikey' => substr($apiKey, 0, 8) . '...', 'allowed_ips' => $apiData['allowed_ips']]);
                errorResponse('Access denied: IP not in whitelist for this API Key', 403);
            }
        }

        //API Functions merge
        try {
            foreach (json_decode($api->offsetGet('functions'), true, 512, JSON_THROW_ON_ERROR) as $function => $enabled) {
                if (array_key_exists($function, $api_functions)) {
                    $api_functions[$function] = $enabled;
                }
            }
        } catch (JsonException $e) {
            $log->error("Failed to decode API functions for API Key: " . $apiKey . " Error: " . $e->getMessage());
        }

        //Check for new functions and update DB
        $update_needed = false;
        try {
            $sql_functions = json_decode($api->offsetGet('functions'), true, 512, JSON_THROW_ON_ERROR);
        } catch (JsonException $e) {
            $log->error("Failed to decode API functions for API Key: " . $apiKey . " Error: " . $e->getMessage());
            $sql_functions = [];
        }

        foreach ($api_functions as $key => $value) {
            if(!array_key_exists($key, $sql_functions)) {
                $update_needed = true;
                $sql_functions[$key] = $value;
            }
        }

        if($update_needed) {
            $log->info("API functions updated for API Key: " . $apiKey, ['functions' => $sql_functions]);
            $database->getExplorer()->table('api')->where('key', $apiKey)->update([
                'functions' => json_encode($sql_functions)
            ]);
        }

        //Set API Data
        try {
            $api_data = json_decode($api->offsetGet('data'), true, 512, JSON_THROW_ON_ERROR);
        } catch (JsonException $e) {
            $log->error("Failed to decode API data for API Key: " . $apiKey . " Error: " . $e->getMessage());
            $api_data = [];
        }

        //Update last used
        $database->getExplorer()->table('api')->where('key', $apiKey)->update([
            'last_used' => new DateTime(),
            'last_ip' => $radd->getClientIP(),
            'last_agent' => $_POST['user_agent'] ?? ($_SERVER['HTTP_USER_AGENT'] ?? 'unknown')
        ]);
    } else {
        //Alle Funktionen erlauben
        foreach ($api_functions as $function => $enabled) {
            $api_functions[$function] = true;
        }
    }
}
elseif (!$requiresApiKey) {
    // Session-basierte Actions benötigen keinen API-Key
    // Alle Funktionen erlauben (werden später in der Handler-Funktion geprüft)
    foreach ($api_functions as $function => $enabled) {
        $api_functions[$function] = true;
    }
} else {
    // Keine IP-Whitelist vorhanden, alle Funktionen erlauben
    foreach ($api_functions as $function => $enabled) {
        $api_functions[$function] = true;
    }
}

// --- Rate Limiting fuer alle API-Anfragen ----------------------------------
try {
    $bfp = \system\helper\BruteForceProtection::getInstance($database);
    $rlCheck = $bfp->isRequestAllowed($radd->getClientIP(), 'api');
    if (!$rlCheck['allowed']) {
        $log->warning('API rate limit exceeded', [
            'ip'     => $radd->getClientIP(),
            'reason' => $rlCheck['reason'],
            'action' => $currentAction,
        ]);
        http_response_code(429);
        header('Retry-After: 60');
        errorResponse($rlCheck['message'], 429);
    }
} catch (\Exception $e) {
    $log->error('Rate limit check failed: ' . $e->getMessage());
    // Bei Fehler im Rate-Limit-Check weiter erlauben (fail-open)
}
// ---------------------------------------------------------------------------

// Logging-Funktion für API-Zugriffe
function logApiAccess(string $action, int $responseCode = 200): void {
    global $database, $current_api_key_id, $current_api_key, $radd, $log, $dkcLog, $requestStartTime;

    $duration = round((microtime(true) - ($requestStartTime ?? microtime(true))) * 1000, 2); // ms

    // Monolog-Eintrag für jede Action (auch ohne API-Key)
    $logContext = [
        'action'        => $action,
        'response_code' => $responseCode,
        'ip'            => $radd->getClientIP(),
        'method'        => $_SERVER['REQUEST_METHOD'] ?? 'GET',
        'duration_ms'   => $duration,
    ];
    if ($current_api_key_id) {
        $logContext['api_key_id'] = $current_api_key_id;
    }

    if ($responseCode >= 500) {
        $log->error('API action completed', $logContext);
    } elseif ($responseCode >= 400) {
        $log->warning('API action rejected', $logContext);
    } else {
        $log->info('API action completed', $logContext);
    }

    if (!$current_api_key_id || !$current_api_key) {
        return; // DB-Log nur wenn ein API-Key verwendet wird
    }

    try {
        $method = $_SERVER['REQUEST_METHOD'] ?? 'GET';
        $requestData = [
            'get' => $_GET,
            'post' => array_diff_key($_POST, ['apikey' => '']), // API-Key nicht loggen
        ];

        // Aktualisiere "last_used" der API-Key
        $database->getExplorer()->table('api')->where('id = ?', $current_api_key_id)
            ->update(['last_used' => new DateTime()]);

        // Log-Eintrag erstellen
        $database->getExplorer()->table('api_log')->insert([
            'api_key_id'   => $current_api_key_id,
            'api_key'      => $current_api_key,
            'action'       => $action,
            'method'       => $method,
            'ip_address'   => $radd->getClientIP(),
            'user_agent'   => $_SERVER['HTTP_USER_AGENT'] ?? null,
            'request_data' => json_encode($requestData),
            'response_code'=> $responseCode,
            'duration_ms'  => $duration,
            'created_at'   => new DateTime()
        ]);

        // Statistik aktualisieren/erstellen
        $stat = $database->getExplorer()->table('api_stats')
            ->where('api_key_id = ? AND action = ?', $current_api_key_id, $action)
            ->fetch();

        if ($stat) {
            $database->getExplorer()->table('api_stats')
                ->where('id = ?', $stat->offsetGet('id'))
                ->update([
                    'call_count' => $stat->offsetGet('call_count') + 1,
                    'last_called' => new DateTime()
                ]);
        } else {
            $database->getExplorer()->table('api_stats')->insert([
                'api_key_id' => $current_api_key_id,
                'action' => $action,
                'call_count' => 1,
                'last_called' => new DateTime(),
                'first_called' => new DateTime()
            ]);
        }
    } catch (Exception $e) {
        $log->error('Failed to log API access: ' . $e->getMessage());
    }

    // DkcDesktop: zusätzlicher Eintrag für non-JSON Endpunkte (Download, RMI etc.)
    // jsonResponse() loggt bereits für JSON-Antworten; hier nur für alles andere
    if (preg_match('/^DkcDesktopClient\//i', $_SERVER['HTTP_USER_AGENT'] ?? '')) {
        $logMethod = $responseCode >= 500 ? 'error' : ($responseCode >= 400 ? 'warning' : 'info');
        $dkcLog->$logMethod('ACTION_COMPLETED', [
            'action'      => $action,
            'http_code'   => $responseCode,
            'duration_ms' => $duration,
            'ip'          => $radd->getClientIP(),
            'method'      => $_SERVER['REQUEST_METHOD'] ?? 'GET',
        ]);
    }
}

function loadIndex(): array {
    $cache = CacheHelper::getInstance();
    $index = $cache->get('sync_index');
    if ($index !== null) {
        return (array)unserialize((string)$index);
    }

    $filePath = _ROOT_ . '/cache/sync_index.json';
    if (file_exists($filePath)) {
        return json_decode(file_get_contents($filePath), true) ?? [];
    }

    return [];
}

function saveIndex(array $index): void {
    global $log;
    $filePath = _ROOT_ . '/cache/sync_index.json';
    try {
        if (file_put_contents($filePath, json_encode($index, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE | JSON_PRETTY_PRINT)) === false) {
            throw new RuntimeException('Failed to save index to file.');
        }
    } catch (JsonException $e) {
        $log->error("Failed to encode sync index to JSON: " . $e->getMessage());
    }

    CacheHelper::getInstance()->set('sync_index', serialize($index), 3600, [CacheHelper::TAG_PROJECTS]); // 1 Stunde TTL
}

// Action Handler Functions
#[NoReturn] function handleSyncDownload($api_functions, $log): void {
    if($api_functions['sync'] === false) {
        logApiAccess('sync_download', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'Sync function is disabled for this API Key.';
        $log->warning("Sync function is disabled for this API Key.");
        exit();
    }

    $log->debug('handleSyncDownload: requested', ['file_id' => $_GET['file'] ?? null]);
    $index = loadIndex();
    if (isset($_GET['file'], $index[$_GET['file']])) {
        $file = $index[$_GET['file']]['file'] ?? null;
        if ($file && file_exists($file)) {
            $hash = $index[$_GET['file']]['hash'] ?? null;
            if ($hash && $hash === md5_file($file)) {
                $log->info('handleSyncDownload: file served', [
                    'file'     => basename($index[$_GET['file']]['name']),
                    'size'     => filesize($file),
                ]);
                logApiAccess('sync_download', 200);
                header('Content-Type: application/pdf');
                header('Content-Disposition: attachment; filename="' . basename($index[$_GET['file']]['name']) . '"');
                header('Content-Length: ' . filesize($file));
                if (!readfile($file)) {
                    header('HTTP/1.1 500 Internal Server Error');
                    echo 'Failed to read file.';
                }
                exit();
            }
        }
    }
    logApiAccess('sync_download', 404);
    $log->warning('handleSyncDownload: file not found', ['file_id' => $_GET['file'] ?? null]);
    header('HTTP/1.1 404 Not Found');
    echo 'File not found.';
    exit();
}

#[NoReturn] function handleSync($api_functions, $log): void {
    if($api_functions['sync'] === false) {
        logApiAccess('sync', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'Sync function is disabled for this API Key.';
        $log->warning("Sync function is disabled for this API Key.");
        exit();
    }

    $log->debug('handleSync: building file list', ['days' => $_GET['days'] ?? null, 'since' => $_GET['since'] ?? null]);

    $baseDir = _ROOT_ . "/data/";
    $baseUrl = "http://rmi.dk-automation.de/api.php?action=sync_download&file=";

    $days = isset($_GET['days']) ? (int)$_GET['days'] : null;
    $since = isset($_GET['since']) ? DateTime::createFromFormat('Y-m-d_H:i', $_GET['since'])->getTimestamp() : null;

    $timeLimit = $days ? time() - ($days * 86400) : ($since ?: null);

    $files = [];
    $iterator = new RecursiveIteratorIterator(
        new RecursiveDirectoryIterator($baseDir, FilesystemIterator::SKIP_DOTS)
    );

    $index = loadIndex();
    foreach ($iterator as $file) {
        if ($file->isFile()) {
            $lastModified = $file->getMTime();
            if ($timeLimit !== null && $lastModified < $timeLimit) {
                continue;
            }

            $relativePath = str_replace("\\", "/", substr($file->getPathname(), strlen($baseDir)));
            $fileId = md5($relativePath);

            $index[$fileId] = [
                "name" => $relativePath,
                "url"  => $baseUrl . $fileId,
                "hash" => md5_file($file->getPathname()),
                "file" => $file->getPathname()
            ];

            $files[] = [
                "name" => $relativePath,
                "url"  => $baseUrl . $fileId,
                "hash" => $index[$fileId]['hash']
            ];
        }
    }

    saveIndex($index);

    $log->info('handleSync: file list built', ['file_count' => count($files)]);
    logApiAccess('sync', 200);
    header('Cache-Control: max-age=3600');
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode($files, JSON_THROW_ON_ERROR | JSON_UNESCAPED_UNICODE);
    exit();
}

#[NoReturn] function handleSMS($api_functions, $api_data, $log): void {
    $log->debug("SMS API called", ['get' => $_GET,'post' => $_POST,'headers' => getallheaders()]);
    if($api_functions['sms'] === false) {
        logApiAccess('sms', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'SMS function is disabled for this API Key.';
        $log->warning("SMS function is disabled for this API Key.");
        exit();
    }

    $ExternalServices = new ExternalServices();
    if(!$ExternalServices->isServiceEnabled('sms')) {
        logApiAccess('sms', 503);
        echo 'Der SMS-Dienst ist zur Zeit nicht verfügbar, bitte kontaktieren Sie den Administrator';
        exit();
    }

    logApiAccess('sms', 200);
    $sender = $_GET['sender'] ?? ($api_data['sms_sender'] ?? 'PRTG');
    $number = $_GET['nummer'] ?? '';
    $text = $_GET['text'] ?? '';

    //Unifi Protect Alarm Manager Integration
    if(($_SERVER['HTTP_USER_AGENT'] ?? '') == 'protect-alarm-manager') {
        /**
         * X-SMS-NUMBER:   Empfänger Telefonnummer(n), bei mehreren Nummern mit Komma trennen
         * X-SMS-TEXT:     Text der SMS
         * X-SMS-SENDER:   Absender der SMS
         */
        $log->debug("Unifi Protect Alarm Manager detected");
        $number = $_SERVER['HTTP_X_SMS_NUMBER'] ?? '';
        $text = $_SERVER['HTTP_X_SMS_TEXT'] ?? '';
        $sender = $_SERVER['HTTP_X_SMS_SENDER'] ?? 'Unifi-Protect';
    }

    $multiSMS = isset($_GET['multi']) && $_GET['multi'] == '1';
    $log->info("Sending SMS", ['nummer' => $number, 'text' => $text, 'sender' => $sender, 'multi' => $multiSMS]);

    if($multiSMS) {
        $numbers = explode(',', $number ?? '');
        $results = [];
        foreach ($numbers as $number) {
            $results[] = ['nummer' => $number,
                'text' => $text ?? '',
                'result' => $ExternalServices->sendSMS(trim($number), $text, $sender)];
        }
        logApiAccess('sms', 200);
        header('Content-Type: application/json; charset=utf-8');
        echo json_encode($results);
        exit();
    }

    logApiAccess('sms', 200);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode([
        'nummer' => $number,
        'text' => $text,
        'result' => $ExternalServices->sendSMS($number,$text,$sender)
    ]);
    exit();
}

#[NoReturn] function handleEmail($api_functions, $log): void {
    $log->debug("E-Mail API called", ['get' => $_GET,'post' => $_POST,'headers' => getallheaders(),'file'=>$_FILES]);
    $address = []; $cc = [];

    if($api_functions['email'] === false) {
        logApiAccess('email', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'E-Mail function is disabled for this API Key.';
        $log->warning("E-Mail function is disabled for this API Key.");
        exit();
    }

    //Unifi Protect Alarm Manager Integration
    if(($_SERVER['HTTP_USER_AGENT'] ?? '') === 'protect-alarm-manager') {
        /**
         * X-EMAIL-ADD:       Empfänger E-Mail Adresse(n), bei mehreren Adressen mit Komma trennen
         * X-EMAIL-CC:        CC E-Mail Adresse(n), bei mehreren Adressen mit Komma trennen
         * X-EMAIL-SUBJECT:   Betreff der E-Mail
         * X-EMAIL-TEMPLATE:  Template Name (z.B. 001_basic)
         * X-EMAIL-TEXT:      Text der E-Mail (wird im Template verwendet)
         */
        $smarty = getSmarty();
        $helper = new UnifiMailHelper($smarty);

        //Check for email headers
        if(array_key_exists('HTTP_X_EMAIL_ADD', $_SERVER)) {
            //Chek of comma separated email addresses
            $emails = explode(',', $_SERVER['HTTP_X_EMAIL_ADD']);
            if(count($emails) >= 2) {
                foreach ($emails as $email) {
                    $address[] = ['email' => trim($email), 'name' => ''];
                }
            } else {
                $address[] = ['email' => trim($_SERVER['HTTP_X_EMAIL_ADD']), 'name' => ''];
            }
        }

        //Check for email headers
        if(array_key_exists('HTTP_X_EMAIL_CC', $_SERVER)) {
            //Chek of comma separated email addresses
            $emails = explode(',', $_SERVER['HTTP_X_EMAIL_CC']);
            if(count($emails) >= 2) {
                foreach ($emails as $email) {
                    $cc[] = ['email' => trim($email), 'name' => ''];
                }
            } else {
                $cc[] = ['email' => trim($_SERVER['HTTP_X_EMAIL_CC']), 'name' => ''];
            }
        }

        $text = $_SERVER['HTTP_X_EMAIL_TEXT'] ?? '';
        $template = strtolower(trim($_SERVER['HTTP_X_EMAIL_TEMPLATE'])) ?? '001_basic';
        $subject = trim($_SERVER['HTTP_X_EMAIL_SUBJECT']) ?? 'Unifi Protect Alarm';
        $log->info("Sending E-Mail via Unifi Protect Alarm Manager",
            ['address' => $address, 'cc' => $cc, 'subject' => $subject,
                'template' => $template, 'text' => $text]);

        try {
            $helper->setDev(DEBUG);
            $helper->init($address, $cc, $subject, $template, $text);
        } catch (\Smarty\Exception $e) {
            $log->error("Smarty Exception: " . $e->getMessage());
            header('HTTP/1.1 500 Internal Server Error');
            try {
                echo json_encode(['success' => false, 'error' => 'Smarty Exception: ' . $e->getMessage()], JSON_THROW_ON_ERROR);
            } catch (JsonException $e) {
                echo '{"success": false, "error": "Smarty Exception and JSON encoding failed."}';
            }
        }
        logApiAccess('email', 200);
        exit();
    }

    // Standard E-Mail API
    $subject = $_POST['subject'] ?? 'DKC: E-Mail';
    if(array_key_exists('to', $_POST)) {
        //Chek of comma separated email addresses
        $emails = explode(',', $_POST['to']);
        if(count($emails) >= 2) {
            foreach ($emails as $email) {
                $address[] = ['email' => trim($email), 'name' => ''];
            }
        } else {
            $address[] = ['email' => trim($_POST['to']), 'name' => ''];
        }
    }

    if(array_key_exists('cc', $_POST)) {
        //Chek of comma separated email addresses
        $emails = explode(',', $_POST['cc']);
        if(count($emails) >= 2) {
            foreach ($emails as $email) {
                $cc[] = ['email' => trim($email), 'name' => ''];
            }
        } else {
            $cc[] = ['email' => trim($_POST['cc']), 'name' => ''];
        }
    }

    $body = $_POST['body'] ?? 'Dies ist eine Test-E-Mail von system.';
    $log->info("Sending E-Mail",
        ['address' => $address, 'cc' => $cc, 'subject' => $subject, 'body' => $body]);

    $email = new MailHelper();
    $email->setDev(DEBUG);
    $email->sendEMail($subject,$body,$address,$cc,[],[]);
    logApiAccess('email', 200);
    exit();
}

#[NoReturn] function handleGotify($api_functions, $log): void {
    $log->debug("Gotify API called", ['get' => $_GET,'post' => $_POST,'headers' => getallheaders()]);

    if($api_functions['gotify'] === false) {
        logApiAccess('gotify', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'Gotify function is disabled for this API Key.';
        $log->warning("Gotify function is disabled for this API Key.");
        exit();
    }

    //Unifi Protect Alarm Manager Integration
    if(($_SERVER['HTTP_USER_AGENT'] ?? '') == 'protect-alarm-manager') {
        /**
         * X-GOTIFY-TITLE:   Titel der Nachricht
         * X-GOTIFY-TEXT:    Text der Nachricht
         * X-GOTIFY-PRIO:    Priorität der Nachricht (0 bis 10)
         * X-GOTIFY-SYSTEM:  Name des Systems (CK oder PROTECT)
         */
        $log->debug("Unifi Protect Alarm Manager detected");
        $title = $_SERVER['HTTP_X_GOTIFY_TITLE'] ?? '';
        $text = $_SERVER['HTTP_X_GOTIFY_TEXT'] ?? '';
        $priority = $_SERVER['HTTP_X_GOTIFY_PRIO'] ?? 0;
        $token = $_SERVER['HTTP_X_GOTIFY_SYSTEM'] ?? 'CK';

        //Validate priority
        if(!is_numeric($priority) || (int)$priority < 0 || (int)$priority > 10) {
            $priority = 0;
        }

        $gotify = GotifyHelper::getInstance(strtolower($token) == 'ck' ? GotifyHelper::KEY_CKEY : GotifyHelper::KEY_PROTECT);
        $gotify->sendMessage($title,$text,$priority);
        logApiAccess('gotify', 200);
        exit();
    }

    // Standard Gotify API
    $title = $_POST['title'] ?? 'system Notification';
    $text = $_POST['text'] ?? 'This is a test message from system.';
    $priority = isset($_POST['priority']) ? (int)$_POST['priority'] : 0;
    $token = $_POST['token'] ?? '';

    //Validate priority
    if(!is_numeric($priority) || (int)$priority < 0 || (int)$priority > 10) {
        $priority = 0;
    }

    $gotify = GotifyHelper::getInstance($token);
    $gotify->sendMessage($title,$text,$priority);
    logApiAccess('gotify', 200);
    exit();
}

function handleRMI($api_functions, $log): void {
    $log->debug('handleRMI: called', ['get' => $_GET, 'post' => array_diff_key($_POST, ['apikey' => ''])]);
    if($api_functions['rmi'] === false) {
        logApiAccess('rmi', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'system function is disabled for this API Key.';
        $log->warning("RMI function is disabled for this API Key.");
        exit();
    }

    $log->info('handleRMI: executing RMI call');
    logApiAccess('rmi', 200);
    $rmi = new RMI();
    $rmi->call();
}

#[NoReturn] function handleWebhook($api_functions, $log, $database, $radd): void {
    global $api_data;

    $log->debug("Webhook API called", ['get' => $_GET, 'post' => $_POST, 'headers' => getallheaders()]);

    if($api_functions['webhook'] === false) {
        logApiAccess('webhook', 403);
        header('HTTP/1.1 403 Forbidden');
        echo 'Webhook function is disabled for this API Key.';
        $log->warning("Webhook function is disabled for this API Key.");
        exit();
    }

    // Ermittle die Quelle aus GET-Parameter oder verwende Standard
    $source = $_GET['source'] ?? ($api_data['webhook_source'] ?? 'unifi_controller');
    $method = $_SERVER['REQUEST_METHOD'];

    // Sammle alle Daten
    $headers = getallheaders();
    $queryParams = $_GET;
    $postData = $_POST;
    $rawBody = file_get_contents('php://input');

    // Versuche JSON aus dem Body zu parsen
    $jsonData = null;
    if (!empty($rawBody)) {
        try {
            $jsonData = json_decode($rawBody, true, 512, JSON_THROW_ON_ERROR);

            // XSS-Prüfung für dekodierte JSON-Daten
            if (is_array($jsonData)) {
                $security = SecurityHardening::getInstance($log);
                array_walk_recursive($jsonData, function($item) use ($security) {
                    if (is_string($item)) {
                        $security->detectXSS($item);
                    }
                });
            }
        } catch (JsonException $e) {
            $log->debug("Failed to parse JSON body: " . $e->getMessage());
        }
    }

    // Kombiniere POST und JSON Daten
    if ($jsonData && is_array($jsonData)) {
        $postData = array_merge($postData, $jsonData);
    }

    // Versuche Event-Typ zu ermitteln
    $eventType = null;

    // Unifi Controller Event-Typ Erkennung
    if (isset($postData['event'])) {
        $eventType = $postData['event'];
    } elseif (isset($postData['type'])) {
        $eventType = $postData['type'];
    } elseif (isset($postData['event_type'])) {
        $eventType = $postData['event_type'];
    } elseif (isset($queryParams['event'])) {
        $eventType = $queryParams['event'];
    }

    $log->info("Webhook received", [
        'source' => $source,
        'event_type' => $eventType,
        'method' => $method,
        'ip' => $radd->getClientIP()
    ]);

    // Speichere Webhook-Log
    try {
        $webhookLog = $database->getExplorer()->table('webhook_logs')->insert([
            'source' => $source,
            'event_type' => $eventType,
            'method' => $method,
            'ip_address' => $radd->getClientIP(),
            'user_agent' => $_SERVER['HTTP_USER_AGENT'] ?? null,
            'headers' => json_encode($headers),
            'query_params' => json_encode($queryParams),
            'post_data' => json_encode($postData),
            'raw_body' => $rawBody,
            'processed' => 0,
            'created_at' => new DateTime()
        ]);

        $webhookLogId = $webhookLog->id ?? null;

        // Aktualisiere Statistiken
        $today = date('Y-m-d');
        $stat = $database->getExplorer()->table('webhook_stats')
            ->where('source = ? AND event_type = ? AND date = ?', $source, $eventType, $today)
            ->fetch();

        if ($stat) {
            $database->getExplorer()->table('webhook_stats')
                ->where('id = ?', $stat->offsetGet('id'))
                ->update([
                    'call_count' => $stat->offsetGet('call_count') + 1,
                    'last_called' => new DateTime()
                ]);
        } else {
            $database->getExplorer()->table('webhook_stats')->insert([
                'source' => $source,
                'event_type' => $eventType,
                'date' => $today,
                'call_count' => 1,
                'last_called' => new DateTime()
            ]);
        }

        // Finde passende Konfigurationen
        $configs = $database->getExplorer()->table('webhook_config')
            ->where('enabled = 1')
            ->where('source = ?', $source);

        if ($eventType) {
            // Entweder spezifisches Event oder alle Events (NULL)
            $configs = $configs->where('(event_type = ? OR event_type IS NULL)', $eventType);
        }

        $notificationsSent = [];

        foreach ($configs as $config) {
            // Prüfe Filter-Bedingungen
            $filterConditions = null;
            if ($config->offsetGet('filter_conditions')) {
                try {
                    $filterConditions = json_decode($config->offsetGet('filter_conditions'), true, 512, JSON_THROW_ON_ERROR);
                } catch (JsonException $e) {
                    $log->error("Failed to parse filter conditions for config " . $config->offsetGet('id'));
                    continue;
                }

                // Prüfe ob alle Bedingungen erfüllt sind
                $filterMatch = true;
                if ($filterConditions && is_array($filterConditions)) {
                    foreach ($filterConditions as $key => $value) {
                        if (!isset($postData[$key]) || $postData[$key] != $value) {
                            $filterMatch = false;
                            break;
                        }
                    }
                }

                if (!$filterMatch) {
                    continue;
                }
            }

            // Update webhook_log mit config_id
            if ($webhookLogId) {
                $database->getExplorer()->table('webhook_logs')
                    ->where('id = ?', $webhookLogId)
                    ->update(['config_id' => $config->offsetGet('id')]);
            }

            // Sende Benachrichtigungen
            $ExternalServices = new ExternalServices();

            // E-Mail
            if ($config->offsetGet('notify_email') && $config->offsetGet('email_addresses')) {
                try {
                    $emails = json_decode($config->offsetGet('email_addresses'), true, 512, JSON_THROW_ON_ERROR);
                    if ($emails && is_array($emails)) {
                        $subject = "Webhook: {$source}" . ($eventType ? " - {$eventType}" : "");
                        $body = "Ein Webhook wurde empfangen:\n\n";
                        $body .= "Quelle: {$source}\n";
                        $body .= "Event: " . ($eventType ?? 'Unbekannt') . "\n";
                        $body .= "Zeit: " . date('d.m.Y H:i:s') . "\n\n";
                        $body .= "Daten:\n" . json_encode($postData, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE);

                        $email = new MailHelper();
                        $addressList = array_map(fn($e) => ['email' => $e, 'name' => ''], $emails);
                        $email->sendEMail($subject, $body, $addressList, [], [], []);
                        $notificationsSent[] = 'email';
                    }
                } catch (Exception $e) {
                    $log->error("Failed to send email notification: " . $e->getMessage());
                }
            }

            // SMS
            if ($config->offsetGet('notify_sms') && $config->offsetGet('sms_numbers')) {
                try {
                    $numbers = json_decode($config->offsetGet('sms_numbers'), true, 512, JSON_THROW_ON_ERROR);
                    if ($numbers && is_array($numbers) && $ExternalServices->isServiceEnabled('sms')) {
                        $text = "Webhook: {$source}" . ($eventType ? " - {$eventType}" : "");
                        foreach ($numbers as $number) {
                            $ExternalServices->sendSMS($number, $text, 'Webhook');
                        }
                        $notificationsSent[] = 'sms';
                    }
                } catch (Exception $e) {
                    $log->error("Failed to send SMS notification: " . $e->getMessage());
                }
            }

            // Gotify
            if ($config->offsetGet('notify_gotify')) {
                try {
                    $title = "Webhook: {$source}";
                    $message = ($eventType ? "Event: {$eventType}\n" : "") .
                        "Zeit: " . date('d.m.Y H:i:s');
                    $priority = $config->offsetGet('gotify_priority') ?? 5;
                    $token = $config->offsetGet('gotify_token') ?? 'CK';

                    $gotify = GotifyHelper::getInstance($token);
                    $gotify->sendMessage($title, $message, $priority);
                    $notificationsSent[] = 'gotify';
                } catch (Exception $e) {
                    $log->error("Failed to send Gotify notification: " . $e->getMessage());
                }
            }
        }

        // Update Log mit Benachrichtigungs-Status
        if ($webhookLogId && !empty($notificationsSent)) {
            $database->getExplorer()->table('webhook_logs')
                ->where('id = ?', $webhookLogId)
                ->update([
                    'notification_sent' => 1,
                    'notification_type' => implode(',', $notificationsSent),
                    'notification_status' => json_encode(['sent' => $notificationsSent, 'time' => date('Y-m-d H:i:s')]),
                    'processed' => 1
                ]);
        } elseif ($webhookLogId) {
            $database->getExplorer()->table('webhook_logs')
                ->where('id = ?', $webhookLogId)
                ->update(['processed' => 1]);
        }

    } catch (Exception $e) {
        $log->error("Failed to process webhook: " . $e->getMessage());
        logApiAccess('webhook', 500);
        http_response_code(500);
        echo json_encode(['success' => false, 'error' => $e->getMessage()]);
        exit();
    }

    logApiAccess('webhook', 200);
    header('Content-Type: application/json; charset=utf-8');
    echo json_encode([
        'success' => true,
        'message' => 'Webhook received and processed',
        'source' => $source,
        'event_type' => $eventType
    ]);
    exit();
}

/**
 * Handle Notifications API Request
 * Gibt neue Benachrichtigungen für den eingeloggten Benutzer zurück
 *
 * Diese Funktion verwendet Session-basierte Authentifizierung.
 * Wenn eine gültige Session vorhanden ist, kann die API frei verwendet werden.
 */
#[NoReturn] function handleNotifications($database, $log): void {
    $radd = new RemoteAddress();
    try {
        // Prüfe ob Benutzer eingeloggt ist
        if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
            $log->warning('Notifications API: No valid session', [
                'session_id' => session_id(),
                'session_status' => session_status(),
                'cookies' => $_COOKIE,
                'ip' => $radd->getClientIP()
            ]);

            http_response_code(401);
            header('Content-Type: application/json; charset=utf-8');
            echo json_encode([
                'success' => false,
                'error' => 'Not authenticated',
                'message' => 'Bitte melden Sie sich an, um Benachrichtigungen zu empfangen.'
            ]);
            exit();
        }

        $userId = $_SESSION['id'];
        $lastId = isset($_GET['last_id']) ? (int)$_GET['last_id'] : 0;

        $log->debug("Notifications API: User $userId requesting notifications since ID $lastId");

        // Hole neue Benachrichtigungen
        $notifications = $database->getExplorer()
            ->table('browser_notifications')
            ->where('user_id = ? AND id > ?', $userId, $lastId)
            ->where('sent = ?', 0)
            ->order('id DESC')
            ->limit(10)
            ->fetchAll();

        // Zähle ungelesene Benachrichtigungen (Browser-Notifications)
        $browserUnread = $database->getExplorer()
            ->table('browser_notifications')
            ->where('user_id = ? AND sent = ?', $userId, 0)
            ->count('*');

        // Initialisiere MM-Count
        $mmCount = 0;

        // Versuche User-Instanz zu holen für Berechtigungs-Check
        try {
            $user = User::getInstance($database);
            if ($user->hasPermission('edit_mm_status_freigabe')) {
                $mmCount = $database->getExplorer()
                    ->table('mm_messages')
                    ->where('status = 0 AND scanned = 1')
                    ->count('*');
            }
        } catch (Exception $e) {
            // Ignoriere Fehler bei User-Initialisierung, Count bleibt 0
            $log->warning("Failed to check MM permissions in Notifications API: " . $e->getMessage());
        }

        $totalUnread = $browserUnread + $mmCount;

        // Rechte für Typ-Filterung
        $canViewWebhooks     = isset($user) && $user->hasPermission('admin_api');
        $canViewSystemErrors = isset($user) && ($user->hasPermission('admin_system_logs') || $user->hasPermission('view_logs'));
        $canViewSibe         = isset($user) && $user->hasPermission('kincony_view');

        $result = [];
        foreach ($notifications as $notification) {
            $type = $notification->offsetGet('type') ?? 'info';

            // Typ-basierter Rechte-Check
            if ($type === 'webhook' && !$canViewWebhooks) {
                continue;
            }
            if ($type === 'error' && !$canViewSystemErrors) {
                continue;
            }
            if (in_array($type, ['sibe_alarm', 'sibe_battery'], true) && !$canViewSibe) {
                continue;
            }

            $result[] = [
                'id' => $notification->offsetGet('id'),
                'title' => $notification->offsetGet('title'),
                'message' => $notification->offsetGet('message'),
                'link' => $notification->offsetGet('link'),
                'icon' => $notification->offsetGet('icon'),
                'priority' => $notification->offsetGet('priority'),
                'type' => $type,
                'created_at' => $notification->offsetGet('created_at')->format('Y-m-d H:i:s')
            ];

            // Markiere als gesendet
            $database->getExplorer()
                ->table('browser_notifications')
                ->where('id = ?', $notification->offsetGet('id'))
                ->update(['sent' => 1, 'sent_at' => new DateTime()]);
        }

        header('Content-Type: application/json; charset=utf-8');
        echo json_encode([
            'success' => true,
            'notifications' => $result,
            'total_unread' => $totalUnread
        ]);
        exit();

    } catch (Exception $e) {
        $log->error("Failed to get notifications: " . $e->getMessage());
        http_response_code(500);
        echo json_encode(['success' => false, 'error' => $e->getMessage()]);
        exit();
    }
}

/**
 * Gibt die aktuelle Client-Cache-Version zurück.
 * Clients pollen diesen Endpoint und lösen bei einer neuen Version
 * automatisch eine Browser-Cache-Invalidierung aus.
 */
#[NoReturn] function handleClientCacheVersion($log): void {
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-cache, no-store, must-revalidate');

    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        http_response_code(401);
        echo json_encode(['success' => false, 'error' => 'Not authenticated']);
        exit();
    }

    $versionFile = _ROOT_ . '/cache/client_cache_version.json';
    $version = 0;
    $triggeredBy = null;

    if (file_exists($versionFile)) {
        $data = json_decode(file_get_contents($versionFile), true);
        $version = $data['version'] ?? 0;
        $triggeredBy = $data['triggered_by'] ?? null;
    }

    // ── Client-Tracking ──────────────────────────────────────────────────────
    // Jeden pollenden Client in cache/client_cache_clients.json protokollieren.
    // Key = session_id, damit pro Browser-Tab nur ein Eintrag entsteht.
    try {
        $clientsFile = _ROOT_ . '/cache/client_cache_clients.json';
        $clients = [];
        if (file_exists($clientsFile)) {
            $raw = file_get_contents($clientsFile);
            $clients = json_decode($raw, true) ?: [];
        }

        $sessionKey = session_id() ?: ($_SESSION['id'] . '_' . md5($_SERVER['REMOTE_ADDR'] ?? ''));
        $ua = $_SERVER['HTTP_USER_AGENT'] ?? 'Unbekannt';
        // Gerätekürzel aus User-Agent ableiten
        $device = 'Desktop';
        if (preg_match('/Mobile|Android|iPhone|iPad/i', $ua)) {
            $device = 'Mobile';
        } elseif (preg_match('/Tablet/i', $ua)) {
            $device = 'Tablet';
        }
        // Browser-Name
        $browser = 'Unbekannt';
        if (preg_match('/Firefox\/(\S+)/i', $ua, $m))      { $browser = 'Firefox ' . $m[1]; }
        elseif (preg_match('/Edg\/(\S+)/i', $ua, $m))      { $browser = 'Edge ' . $m[1]; }
        elseif (preg_match('/Chrome\/(\S+)/i', $ua, $m))   { $browser = 'Chrome ' . $m[1]; }
        elseif (preg_match('/Safari\/(\S+)/i', $ua, $m))   { $browser = 'Safari ' . $m[1]; }

        $clients[$sessionKey] = [
            'user_id'    => (int)($_SESSION['id'] ?? 0),
            'username'   => $_SESSION['username'] ?? ('User #' . ($_SESSION['id'] ?? '?')),
            'ip'         => $_SERVER['REMOTE_ADDR'] ?? '?',
            'browser'    => $browser,
            'device'     => $device,
            'last_seen'  => time(),
            'version'    => $version,
        ];

        // Einträge älter als 3 Minuten entfernen (Client hat Tab geschlossen)
        $cutoff = time() - 180;
        $clients = array_filter($clients, static fn($c) => ($c['last_seen'] ?? 0) >= $cutoff);

        file_put_contents($clientsFile, json_encode(array_values($clients)), LOCK_EX);
    } catch (\Throwable $e) {
        $log->warning('Client-Cache-Tracking Fehler: ' . $e->getMessage());
    }
    // ── Ende Client-Tracking ─────────────────────────────────────────────────

    echo json_encode([
        'success'      => true,
        'version'      => $version,
        'triggered_by' => $triggeredBy,
    ]);
    exit();
}

/**
 * Gibt die Anzahl der ungelesenen Benachrichtigungen und eine Vorschau zurück
 * Wird für die Navbar-Badge verwendet und aktualisiert sich automatisch
 */
#[NoReturn] function handleGetNotificationCount($database, $log): void {
    try {
        // Prüfe ob Benutzer eingeloggt ist
        if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
            http_response_code(401);
            header('Content-Type: application/json; charset=utf-8');
            echo json_encode([
                'success' => false,
                'error' => 'Not authenticated',
                'unread_count' => 0
            ], JSON_THROW_ON_ERROR);
            exit();
        }

        $userId = $_SESSION['id'];

        // User-Instanz für Berechtigungs-Checks
        $user = null;
        try {
            $user = User::getInstance($database);
        } catch (Exception $e) {
            $log->warning("handleGetNotificationCount: Failed to load user: " . $e->getMessage());
        }

        // Rechte für Typ-Filterung
        $canViewWebhooks     = $user && $user->hasPermission('admin_api');
        $canViewSystemErrors = $user && ($user->hasPermission('admin_system_logs') || $user->hasPermission('view_logs'));
        $canViewSibe         = $user && $user->hasPermission('kincony_view');

        // Zähle ungelesene Benachrichtigungen
        $unreadCount = $database->getExplorer()
            ->table('browser_notifications')
            ->where('user_id = ? AND sent = ?', $userId, 0)
            ->count('*');

        // Hole die letzten 5 ungelesenen Benachrichtigungen für die Vorschau
        $recentNotifications = $database->getExplorer()
            ->table('browser_notifications')
            ->where('user_id = ? AND sent = ?', $userId, 0)
            ->order('created_at DESC')
            ->limit(5)
            ->fetchAll();

        $notifications = [];
        foreach ($recentNotifications as $notification) {
            $type = $notification->offsetGet('type') ?? 'info';

            // Typ-basierter Rechte-Check
            if ($type === 'webhook' && !$canViewWebhooks) {
                $unreadCount = max(0, $unreadCount - 1);
                continue;
            }
            if ($type === 'error' && !$canViewSystemErrors) {
                $unreadCount = max(0, $unreadCount - 1);
                continue;
            }
            if (in_array($type, ['sibe_alarm', 'sibe_battery'], true) && !$canViewSibe) {
                $unreadCount = max(0, $unreadCount - 1);
                continue;
            }

            $createdAt = $notification->offsetGet('created_at');
            $timeAgo = '';

            if ($createdAt instanceof DateTime) {
                $now = new DateTime();
                $diff = $now->diff($createdAt);

                if ($diff->days > 0) {
                    $timeAgo = $diff->days . ' Tag' . ($diff->days > 1 ? 'e' : '') . ' her';
                } elseif ($diff->h > 0) {
                    $timeAgo = $diff->h . ' Stunde' . ($diff->h > 1 ? 'n' : '') . ' her';
                } elseif ($diff->i > 0) {
                    $timeAgo = $diff->i . ' Minute' . ($diff->i > 1 ? 'n' : '') . ' her';
                } else {
                    $timeAgo = 'gerade eben';
                }
            }

            $notifications[] = [
                'id' => $notification->offsetGet('id'),
                'text' => $notification->offsetGet('message') ?? $notification->offsetGet('title'),
                'link' => $notification->offsetGet('link') ?? '#',
                'time' => $timeAgo,
                'type' => $type
            ];
        }

        // Freigabe-E-Mails aus mm_messages hinzufügen (EML-Scanner) – nur für berechtigte Nutzer
        if ($user && $user->hasPermission('edit_mm_status_freigabe')) {
            foreach ($database->getExplorer()->table('mm_messages')->where('status = 0 AND scanned = 1')->order('created_at ASC')->fetchAll() as $item) {
                $createdAt = $item->offsetGet('created_at');
                $timeAgo = '';

                if ($createdAt instanceof DateTime) {
                    $now = new DateTime();
                    $diff = $now->diff($createdAt);

                    if ($diff->days > 0) {
                        $timeAgo = $diff->days . ' Tag' . ($diff->days > 1 ? 'e' : '') . ' her';
                    } elseif ($diff->h > 0) {
                        $timeAgo = $diff->h . ' Stunde' . ($diff->h > 1 ? 'n' : '') . ' her';
                    } elseif ($diff->i > 0) {
                        $timeAgo = $diff->i . ' Minute' . ($diff->i > 1 ? 'n' : '') . ' her';
                    } else {
                        $timeAgo = 'gerade eben';
                    }
                }

                $notifications[] = [
                    'id' => $item->offsetGet('id'),
                    'text' => 'Eine neue Freigabe für DKC-ID: ' . $item->offsetGet('uid'),
                    'time' => $timeAgo,
                    'link' => '?page=mm&action=view&uid=' .$item->offsetGet('uid').'&auto=1',
                    'type' => 'info'
                ];
                $unreadCount++;
            }
        } // end if edit_mm_status_freigabe

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        header('Pragma: no-cache');
        header('Expires: 0');

        echo json_encode([
            'success' => true,
            'unread_count' => $unreadCount,
            'notifications' => $notifications
        ]);
        exit();

    } catch (Exception $e) {
        $log->error("Failed to get notification count: " . $e->getMessage());
        http_response_code(500);
        echo json_encode([
            'success' => false,
            'error' => $e->getMessage(),
            'unread_count' => 0
        ]);
        exit();
    }
}

/**
 * CKEditor 5 Autosave – Draft in der Session sichern / laden / löschen.
 *
 * POST   api.php?action=ckeditor_draft          Body: {"element_id":"…","content":"…"}
 * GET    api.php?action=ckeditor_draft&element_id=…
 * DELETE api.php?action=ckeditor_draft&element_id=…
 */
function handleCKEditorDraft($database): never {
    global $log;
    $log->debug('handleCKEditorDraft: called', ['method' => $_SERVER['REQUEST_METHOD'] ?? 'GET', 'element_id' => $_GET['element_id'] ?? null]);
    // Authentifizierung über User-Klasse (behandelt Session + dk_autologin-Cookie)
    try {
        $user = User::getInstance($database);
    } catch (\Throwable $e) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }
    if (!$user->isLoggedIn()) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $method = strtoupper($_SERVER['REQUEST_METHOD']);

    // Element-ID aus Query-String oder JSON-Body lesen
    if ($method === 'POST') {
        // file_get_contents('php://input') vermeiden (kann in Produktion deaktiviert sein)
        $rawInput = stream_get_contents(fopen('php://input', 'rb') ?: STDIN);
        $input     = json_decode($rawInput ?: '{}', true) ?? [];
        $elementId = trim($input['element_id'] ?? '');
        $content   = $input['content']    ?? '';
    } else {
        $elementId = trim($_GET['element_id'] ?? '');
        $content   = '';
    }

    // Element-ID validieren (nur a-z, A-Z, 0-9, _)
    if (!preg_match('/^[a-zA-Z0-9_]{1,64}$/', $elementId)) {
        jsonResponse(['success' => false, 'error' => 'Ungültige Element-ID'], 400);
    }

    // Session-Namespace pro Benutzer
    $userId   = (int)$_SESSION['id'];
    $draftKey = 'ckeditor_draft_' . $userId;

    if ($method === 'POST') {
        // Inhalt auf erlaubte HTML-Tags beschränken (CKEditor-Ausgabe aller aktiven Plugins)
        $allowed = implode('', [
            '<p><br><strong><em><u><s>',           // Basis-Formatierung
            '<ul><ol><li>',                         // Listen
            '<span>',                               // FontSize, FontColor (inline styles)
            '<mark>',                               // Highlight
            '<table><thead><tbody><tfoot>',         // Tabellen
            '<tr><th><td><caption><colgroup><col>', // Tabellen-Elemente
            '<figure><figcaption><oembed>',         // Media Embed
            '<div>',                                // Alignment-Wrapper
        ]);
        $sanitized = strip_tags($content, $allowed);

        if (!isset($_SESSION[$draftKey])) {
            $_SESSION[$draftKey] = [];
        }
        $_SESSION[$draftKey][$elementId] = $sanitized;
        jsonResponse(['success' => true]);

    } elseif ($method === 'DELETE') {
        unset($_SESSION[$draftKey][$elementId]);
        jsonResponse(['success' => true]);

    } else {
        // GET – Draft zurückgeben
        $saved = $_SESSION[$draftKey][$elementId] ?? '';
        jsonResponse(['success' => true, 'content' => $saved]);
    }
}

// ============================================================================
// ZÄHLERERFASSUNG – PWA Offline-fähige Endpunkte
// Alle Endpunkte verwenden Session-basierte Authentifizierung (kein API-Key).
// ============================================================================

/**
 * Gibt die Zählerliste des aktiven Projekts zurück.
 * Die PWA speichert diese Daten in IndexedDB für den Offline-Betrieb.
 *
 * GET api.php?action=meter_list[&project_id=X][&include_inactive=1]
 *
 * Hinweis: QR-Codes werden NICHT serverseitig generiert.
 * Das Feld `qr_code` enthält nur extern vergebene Werte (z. B. aufgeklebter Code).
 */
#[NoReturn] function handleMeterList($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId       = isset($_GET['project_id'])      ? (int)$_GET['project_id']      : (int)($_SESSION['active_project_id'] ?? 1);
        $includeInactive = isset($_GET['include_inactive']) && $_GET['include_inactive'] === '1';

        $query = $database->getExplorer()
            ->table('mm_meter')
            ->where('project_id', $projectId)
            ->order('building_id ASC, name ASC');

        if (!$includeInactive) {
            $query->where('is_active', 1);
        }

        $result = [];
        foreach ($query->fetchAll() as $m) {
            $result[] = [
                'id'                  => (int)$m->offsetGet('id'),
                'meter_number'        => $m->offsetGet('meter_number'),
                'name'                => $m->offsetGet('name'),
                'meter_type'          => $m->offsetGet('meter_type'),
                'unit'                => $m->offsetGet('unit'),
                'manufacturer'        => $m->offsetGet('manufacturer'),
                'building_id'         => $m->offsetGet('building_id') !== null ? (int)$m->offsetGet('building_id') : null,
                'whg_id'              => $m->offsetGet('whg_id') !== null ? (int)$m->offsetGet('whg_id') : null,
                'location'            => $m->offsetGet('location'),
                'purpose'             => $m->offsetGet('purpose'),
                // QR-Code: nur externer Wert, keine Generierung
                'qr_code'             => $m->offsetGet('qr_code') ?: null,
                'last_reading_value'  => $m->offsetGet('reading_value') !== null ? (float)$m->offsetGet('reading_value') : null,
                'last_reading_date'   => $m->offsetGet('reading_date'),
                'notes'               => $m->offsetGet('notes'),
                'is_active'           => (bool)$m->offsetGet('is_active'),
                'deactivated_reason'  => $m->offsetGet('deactivated_reason'),
                'deactivated_at'      => $m->offsetGet('deactivated_at'),
            ];
        }

        $log->debug('meter_list returned ' . count($result) . ' meters', ['project_id' => $projectId, 'include_inactive' => $includeInactive]);

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        echo json_encode([
            'success'    => true,
            'project_id' => $projectId,
            'meters'     => $result,
            'synced_at'  => date('Y-m-d H:i:s'),
        ], JSON_UNESCAPED_UNICODE);
        exit();
    } catch (Exception $e) {
        $log->error('meter_list Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Gibt alle Zähler mit gesetztem QR-Code-Wert zurück.
 * Dient zur Anzeige / zum Ausdrucken der QR-Code-Übersicht.
 * QR-Codes werden NICHT generiert – es werden nur gespeicherte externe Werte zurückgegeben.
 *
 * GET api.php?action=meter_qr_list[&project_id=X]
 */
#[NoReturn] function handleMeterQrList($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId = isset($_GET['project_id']) ? (int)$_GET['project_id'] : (int)($_SESSION['active_project_id'] ?? 1);

        $meters = $database->getExplorer()
            ->table('mm_meter')
            ->where('project_id', $projectId)
            ->where('is_active', 1)
            ->where('qr_code != ?', '')
            ->where('qr_code IS NOT NULL')
            ->order('building_id ASC, name ASC')
            ->fetchAll();

        $result = [];
        foreach ($meters as $m) {
            $result[] = [
                'id'          => (int)$m->offsetGet('id'),
                'meter_number'=> $m->offsetGet('meter_number'),
                'name'        => $m->offsetGet('name'),
                'meter_type'  => $m->offsetGet('meter_type'),
                'unit'        => $m->offsetGet('unit'),
                'building_id' => $m->offsetGet('building_id') !== null ? (int)$m->offsetGet('building_id') : null,
                'location'    => $m->offsetGet('location'),
                'purpose'     => $m->offsetGet('purpose'),
                'qr_code'     => $m->offsetGet('qr_code'),   // Externer QR-Code-Wert
            ];
        }

        $log->debug('meter_qr_list returned ' . count($result) . ' meters with QR code', ['project_id' => $projectId]);

        jsonResponse([
            'success'    => true,
            'project_id' => $projectId,
            'total'      => count($result),
            'meters'     => $result,
        ]);
    } catch (Exception $e) {
        $log->error('meter_qr_list Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Deaktiviert einen Messpunkt ("Messpunkt entfernen") mit Pflichtbegründung.
 * Der Datensatz bleibt in der Datenbank erhalten (is_active = 0).
 *
 * POST api.php?action=meter_deactivate
 * Body JSON: { "meter_id": 5, "reason": "Zähler ausgebaut am 2026-04-21" }
 */
#[NoReturn] function handleMeterDeactivate($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $rawInput = file_get_contents('php://input');
    try {
        $input = json_decode($rawInput ?: '{}', true, 512, JSON_THROW_ON_ERROR);
    } catch (JsonException $e) {
        jsonResponse(['success' => false, 'error' => 'Ungültige JSON-Eingabe'], 400);
    }

    $meterId   = isset($input['meter_id']) ? (int)$input['meter_id'] : null;
    $reason    = trim($input['reason'] ?? '');
    $userId    = (int)$_SESSION['id'];
    $projectId = (int)($_SESSION['active_project_id'] ?? 1);

    if (!$meterId) {
        jsonResponse(['success' => false, 'error' => 'meter_id fehlt'], 400);
    }
    if (mb_strlen($reason) < 5) {
        jsonResponse(['success' => false, 'error' => 'Eine Begründung (mind. 5 Zeichen) ist erforderlich'], 400);
    }

    try {
        $meter = $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->where('project_id', $projectId)
            ->fetch();

        if (!$meter) {
            jsonResponse(['success' => false, 'error' => 'Messpunkt nicht gefunden'], 404);
        }
        if (!(bool)$meter->offsetGet('is_active')) {
            jsonResponse(['success' => false, 'error' => 'Messpunkt ist bereits deaktiviert'], 409);
        }

        $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->update([
                'is_active'           => 0,
                'deactivated_reason'  => mb_substr($reason, 0, 2000),
                'deactivated_at'      => date('Y-m-d H:i:s'),
                'deactivated_by'      => $userId,
                'updated_at'          => date('Y-m-d H:i:s'),
            ]);

        $log->info('meter_deactivate: Messpunkt deaktiviert', [
            'meter_id'  => $meterId,
            'reason'    => $reason,
            'user_id'   => $userId,
        ]);

        jsonResponse([
            'success' => true,
            'message' => 'Messpunkt wurde entfernt',
            'meter_id'=> $meterId,
        ]);
    } catch (Exception $e) {
        $log->error('meter_deactivate Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Reaktiviert einen zuvor deaktivierten Messpunkt.
 *
 * POST api.php?action=meter_activate
 * Body JSON: { "meter_id": 5 }
 */
#[NoReturn] function handleMeterActivate($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $rawInput = file_get_contents('php://input');
    try {
        $input = json_decode($rawInput ?: '{}', true, 512, JSON_THROW_ON_ERROR);
    } catch (JsonException $e) {
        jsonResponse(['success' => false, 'error' => 'Ungültige JSON-Eingabe'], 400);
    }

    $meterId   = isset($input['meter_id']) ? (int)$input['meter_id'] : null;
    $userId    = (int)$_SESSION['id'];
    $projectId = (int)($_SESSION['active_project_id'] ?? 1);

    if (!$meterId) {
        jsonResponse(['success' => false, 'error' => 'meter_id fehlt'], 400);
    }

    try {
        $meter = $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->where('project_id', $projectId)
            ->fetch();

        if (!$meter) {
            jsonResponse(['success' => false, 'error' => 'Messpunkt nicht gefunden'], 404);
        }
        if ((bool)$meter->offsetGet('is_active')) {
            jsonResponse(['success' => false, 'error' => 'Messpunkt ist bereits aktiv'], 409);
        }

        $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->update([
                'is_active'          => 1,
                'deactivated_reason' => null,
                'deactivated_at'     => null,
                'deactivated_by'     => null,
                'updated_at'         => date('Y-m-d H:i:s'),
            ]);

        $log->info('meter_activate: Messpunkt reaktiviert', ['meter_id' => $meterId, 'user_id' => $userId]);

        jsonResponse([
            'success'  => true,
            'message'  => 'Messpunkt wurde reaktiviert',
            'meter_id' => $meterId,
        ]);
    } catch (Exception $e) {
        $log->error('meter_activate Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Gibt alle Gebäude des aktiven Projekts aus project_buildings zurück.
 * Dient als Offline-Datenbasis für die PWA-Dropdown-Auswahl.
 *
 * GET api.php?action=meter_buildings[&project_id=X]
 */
#[NoReturn] function handleMeterBuildings($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId = isset($_GET['project_id'])
            ? (int)$_GET['project_id']
            : (int)($_SESSION['active_project_id'] ?? 1);

        $rows = $database->getExplorer()
            ->table('project_buildings')
            ->where('project_id', $projectId)
            ->order('sortierung ASC, name ASC')
            ->fetchAll();

        $result = [];
        foreach ($rows as $row) {
            $result[] = [
                'id'          => (int)$row->offsetGet('id'),
                'building_id' => (int)$row->offsetGet('building_id'),
                'name'        => $row->offsetGet('name'),
                'address'     => $row->offsetGet('address'),
            ];
        }

        $log->debug('meter_buildings returned ' . count($result) . ' buildings', ['project_id' => $projectId]);

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        echo json_encode([
            'success'    => true,
            'project_id' => $projectId,
            'buildings'  => $result,
            'synced_at'  => date('Y-m-d H:i:s'),
        ], JSON_UNESCAPED_UNICODE);
        exit();
    } catch (Exception $e) {
        $log->error('meter_buildings Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Gibt alle Wohneinheiten aus mm_whg zurück (optional nach Haus-ID gefiltert).
 * Dient als Offline-Datenbasis für die PWA-Dropdown-Auswahl.
 *
 * GET api.php?action=meter_whg[&project_id=X][&building_id=Y]
 */
#[NoReturn] function handleMeterWhg($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId  = isset($_GET['project_id'])
            ? (int)$_GET['project_id']
            : (int)($_SESSION['active_project_id'] ?? 1);
        $buildingId = isset($_GET['building_id']) ? (int)$_GET['building_id'] : null;

        $query = $database->getExplorer()
            ->table('mm_whg')
            ->where('project_id', $projectId)
            ->order('haus ASC, sorted ASC, value ASC');

        if ($buildingId !== null && $buildingId > 0) {
            $query->where('haus', $buildingId);
        }

        $result = [];
        foreach ($query->fetchAll() as $row) {
            $result[] = [
                'id'      => (int)$row->offsetGet('id'),
                'haus'    => (int)$row->offsetGet('haus'),
                'value'   => $row->offsetGet('value'),
                'name'    => $row->offsetGet('name'),
                'empty'   => (bool)$row->offsetGet('empty'),
                'sonder'  => (bool)$row->offsetGet('sonder'),
                'keller'  => $row->offsetGet('keller'),
                'sorted'  => (int)$row->offsetGet('sorted'),
            ];
        }

        $log->debug('meter_whg returned ' . count($result) . ' units', ['project_id' => $projectId, 'building_id' => $buildingId]);

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        echo json_encode([
            'success'     => true,
            'project_id'  => $projectId,
            'building_id' => $buildingId,
            'units'       => $result,
            'synced_at'   => date('Y-m-d H:i:s'),
        ], JSON_UNESCAPED_UNICODE);
        exit();
    } catch (Exception $e) {
        $log->error('meter_whg Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Gibt alle aktiven Benutzer zurück (aus der `users`-Tabelle).
 * Dient als Offline-Datenbasis für die PWA (z. B. Erfasser-Auswahl).
 *
 * GET api.php?action=meter_users
 */
#[NoReturn] function handleMeterUsers($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $rows = $database->getExplorer()
            ->table('users')
            ->where('enabled', 1)
            ->order('nname ASC, vname ASC')
            ->fetchAll();

        $result = [];
        foreach ($rows as $row) {
            $permissions = json_decode((string)($row->offsetGet('permissions') ?? '{}'), true) ?? [];
            $result[] = [
                'id'       => (int)$row->offsetGet('id'),
                'username' => (string)$row->offsetGet('username'),
                'name'     => trim(((string)($row->offsetGet('vname') ?? '')) . ' ' . ((string)($row->offsetGet('nname') ?? ''))),
                'email'    => (string)($row->offsetGet('email') ?? ''),
                'is_admin' => !empty($permissions['admin']),
            ];
        }

        $log->debug('meter_users returned ' . count($result) . ' users');

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        echo json_encode([
            'success'   => true,
            'users'     => $result,
            'synced_at' => date('Y-m-d H:i:s'),
        ], JSON_UNESCAPED_UNICODE);
        exit();
    } catch (Exception $e) {
        $log->error('meter_users Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Kombinierter Dropdown-Endpunkt: Gebäude → Wohnungen → Mieter.
 * Gibt alle Gebäude des Projekts mit ihren Wohneinheiten (inkl. Mietername) zurück.
 * Ideal für kaskadierende Dropdown-Menüs in DKC-Desktop und anderen Clients.
 *
 * GET api.php?action=dropdown_data[&project_id=X][&include_empty=0|1][&flat=0|1]
 *
 * Parameter:
 *   project_id    – Projekt-ID (Default: Session-Projekt)
 *   include_empty – Leerstände einschließen (0=nein, 1=ja; Default: 1)
 *   flat          – Flaches Array statt hierarchisch (0=hierarchisch, 1=flach; Default: 0)
 */
#[NoReturn] function handleDropdownData($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId    = isset($_GET['project_id'])
            ? (int)$_GET['project_id']
            : (int)($_SESSION['active_project_id'] ?? 1);
        $includeEmpty = !isset($_GET['include_empty']) || (bool)(int)$_GET['include_empty'];
        $flat         = isset($_GET['flat']) && (bool)(int)$_GET['flat'];

        // ── Gebäude laden ────────────────────────────────────────────────────────
        $buildings = $database->getExplorer()
            ->table('project_buildings')
            ->where('project_id', $projectId)
            ->order('sortierung ASC, name ASC')
            ->fetchAll();

        // ── Wohnungen laden ──────────────────────────────────────────────────────
        $whgQuery = $database->getExplorer()
            ->table('mm_whg')
            ->where('project_id', $projectId)
            ->order('haus ASC, sorted ASC, value ASC');

        if (!$includeEmpty) {
            $whgQuery->where('empty', 0);
        }

        // Wohnungen nach Haus-ID gruppieren
        $whgByHaus = [];
        foreach ($whgQuery->fetchAll() as $w) {
            $hausId = (int)$w->offsetGet('haus');
            $whgByHaus[$hausId][] = [
                'id'     => (int)$w->offsetGet('id'),
                'value'  => (string)($w->offsetGet('value') ?? ''),
                'mieter' => (string)($w->offsetGet('name') ?? ''),
                'empty'  => (bool)$w->offsetGet('empty'),
                'sonder' => (bool)$w->offsetGet('sonder'),
                'keller' => (string)($w->offsetGet('keller') ?? ''),
                'sorted' => (int)$w->offsetGet('sorted'),
            ];
        }

        // ── Flaches Format ───────────────────────────────────────────────────────
        if ($flat) {
            $result = [];
            foreach ($buildings as $b) {
                $bid    = (int)$b->offsetGet('building_id');
                $bName  = (string)($b->offsetGet('name') ?? '');
                $bAddr  = (string)($b->offsetGet('address') ?? '');
                foreach ($whgByHaus[$bid] ?? [] as $w) {
                    $result[] = [
                        'building_id'   => $bid,
                        'building_name' => $bName,
                        'building_addr' => $bAddr,
                        'whg_id'        => $w['id'],
                        'whg_value'     => $w['value'],
                        'mieter'        => $w['mieter'],
                        'empty'         => $w['empty'],
                        'sonder'        => $w['sonder'],
                        'keller'        => $w['keller'],
                        'label'         => $bName . ' – ' . $w['value'] . ($w['mieter'] ? ' (' . $w['mieter'] . ')' : ''),
                    ];
                }
            }

            $log->debug('dropdown_data (flat) returned ' . count($result) . ' entries', [
                'project_id'    => $projectId,
                'include_empty' => $includeEmpty,
            ]);

            header('Content-Type: application/json; charset=utf-8');
            header('Cache-Control: no-cache, no-store, must-revalidate');
            echo json_encode([
                'success'    => true,
                'project_id' => $projectId,
                'flat'       => true,
                'items'      => $result,
                'synced_at'  => date('Y-m-d H:i:s'),
            ], JSON_UNESCAPED_UNICODE);
            exit();
        }

        // ── Hierarchisches Format ────────────────────────────────────────────────
        $result = [];
        foreach ($buildings as $b) {
            $bid  = (int)$b->offsetGet('building_id');
            $result[] = [
                'id'          => (int)$b->offsetGet('id'),
                'building_id' => $bid,
                'name'        => (string)($b->offsetGet('name') ?? ''),
                'address'     => (string)($b->offsetGet('address') ?? ''),
                'apartments'  => $whgByHaus[$bid] ?? [],
            ];
        }

        $totalWhg = array_sum(array_map(static fn($b) => count($b['apartments']), $result));
        $log->debug('dropdown_data returned ' . count($result) . ' buildings / ' . $totalWhg . ' apartments', [
            'project_id'    => $projectId,
            'include_empty' => $includeEmpty,
        ]);

        header('Content-Type: application/json; charset=utf-8');
        header('Cache-Control: no-cache, no-store, must-revalidate');
        echo json_encode([
            'success'    => true,
            'project_id' => $projectId,
            'flat'       => false,
            'buildings'  => $result,
            'synced_at'  => date('Y-m-d H:i:s'),
        ], JSON_UNESCAPED_UNICODE);
        exit();

    } catch (Exception $e) {
        $log->error('dropdown_data Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Speichert eine einzelne Zählerablesung.
 *
 * POST api.php?action=meter_submit
 * Body JSON: {
 *   "meter_id": 5,
 *   "reading_value": 1234.567,
 *   "reading_date": "2026-04-21",
 *   "reading_time": "09:30",   // optional
 *   "note": "...",             // optional
 *   "local_id": "uuid-v4",    // optional – für Offline-Deduplizierung
 *   "image_base64": "..."      // optional – Base64-kodiertes Foto
 * }
 */
#[NoReturn] function handleMeterSubmit($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $rawInput = file_get_contents('php://input');
    try {
        $input = json_decode($rawInput ?: '{}', true, 512, JSON_THROW_ON_ERROR);
    } catch (JsonException $e) {
        jsonResponse(['success' => false, 'error' => 'Ungültige JSON-Eingabe'], 400);
    }

    // Pflichtfelder
    $meterId      = isset($input['meter_id'])      ? (int)$input['meter_id']      : null;
    $readingValue = isset($input['reading_value'])  ? (float)$input['reading_value'] : null;
    $readingDate  = trim($input['reading_date']  ?? '');

    if (!$meterId || $readingValue === null || !$readingDate) {
        jsonResponse(['success' => false, 'error' => 'meter_id, reading_value und reading_date sind erforderlich'], 400);
    }

    // Datum validieren
    $parsedDate = \DateTime::createFromFormat('Y-m-d', $readingDate);
    if (!$parsedDate || $parsedDate->format('Y-m-d') !== $readingDate) {
        jsonResponse(['success' => false, 'error' => 'Ungültiges Datumsformat (erwartet YYYY-MM-DD)'], 400);
    }

    $localId     = !empty($input['local_id'])     ? trim(substr($input['local_id'], 0, 64)) : null;
    $note        = !empty($input['note'])          ? trim(substr($input['note'], 0, 1000))   : null;
    $readingTime = !empty($input['reading_time'])  ? trim($input['reading_time'])             : null;
    $userId      = (int)$_SESSION['id'];
    $projectId   = (int)($_SESSION['active_project_id'] ?? 1);

    try {
        // Zähler prüfen & Projekt-Zugehörigkeit sicherstellen
        $meter = $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->where('project_id', $projectId)
            ->fetch();

        if (!$meter) {
            jsonResponse(['success' => false, 'error' => 'Zähler nicht gefunden'], 404);
        }

        // Deduplizierung: Falls local_id bereits existiert, Eintrag zurückgeben
        if ($localId) {
            $existing = $database->getExplorer()->table('mm_meter_readings')
                ->where('local_id', $localId)
                ->fetch();
            if ($existing) {
                jsonResponse([
                    'success'    => true,
                    'reading_id' => (int)$existing->offsetGet('id'),
                    'duplicate'  => true,
                    'message'    => 'Ablesung bereits gespeichert (Deduplizierung via local_id)',
                ]);
            }
        }

        // Foto optional speichern
        $imagePath = null;
        if (!empty($input['image_base64'])) {
            $base64 = $input['image_base64'];
            // DataURI-Prefix entfernen
            if (preg_match('/^data:image\/(jpeg|png|webp);base64,(.+)$/i', $base64, $m)) {
                $ext      = strtolower($m[1]) === 'webp' ? 'webp' : (strtolower($m[1]) === 'png' ? 'png' : 'jpg');
                $imgData  = base64_decode($m[2], true);
                if ($imgData !== false) {
                    $dir  = _ROOT_ . '/data/meter_readings/' . $meterId . '/';
                    if (!is_dir($dir)) {
                        mkdir($dir, 0755, true);
                    }
                    $filename  = date('Ymd_His') . '_' . $userId . '.' . $ext;
                    $fullPath  = $dir . $filename;
                    if (file_put_contents($fullPath, $imgData) !== false) {
                        $imagePath = 'meter_readings/' . $meterId . '/' . $filename;
                    }
                }
            }
        }

        // Ablesung eintragen
        $insertData = [
            'meter_id'      => $meterId,
            'project_id'    => $projectId,
            'reading_value' => $readingValue,
            'reading_date'  => $readingDate,
            'reading_time'  => $readingTime,
            'note'          => $note,
            'image_path'    => $imagePath,
            'created_by'    => $userId,
            'synced_at'     => date('Y-m-d H:i:s'),
            'created_at'    => date('Y-m-d H:i:s'),
        ];
        if ($localId) {
            $insertData['local_id'] = $localId;
        }

        $row = $database->getExplorer()->table('mm_meter_readings')->insert($insertData);

        // Letzten Ablesewert im Zähler-Datensatz aktualisieren
        $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->update([
                'reading_value' => $readingValue,
                'reading_date'  => $readingDate,
                'updated_at'    => date('Y-m-d H:i:s'),
            ]);

        $log->info('meter_submit: Ablesung gespeichert', [
            'meter_id'      => $meterId,
            'reading_value' => $readingValue,
            'user_id'       => $userId,
            'local_id'      => $localId,
        ]);

        jsonResponse([
            'success'    => true,
            'reading_id' => (int)$row->id,
            'message'    => 'Ablesung erfolgreich gespeichert',
        ]);
    } catch (Exception $e) {
        $log->error('meter_submit Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler beim Speichern'], 500);
    }
}

/**
 * Batch-Sync: Überträgt alle offline gespeicherten Ablesungen auf einmal.
 * Ideal nach Wiederherstellung der Internetverbindung in der PWA.
 *
 * POST api.php?action=meter_batch_sync
 * Body JSON: {
 *   "readings": [
 *     { "meter_id": 5, "reading_value": 1234.5, "reading_date": "2026-04-20",
 *       "reading_time": "08:00", "note": "...", "local_id": "uuid-v4" },
 *     ...
 *   ]
 * }
 */
#[NoReturn] function handleMeterBatchSync($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $rawInput = file_get_contents('php://input');
    try {
        $input = json_decode($rawInput ?: '{}', true, 512, JSON_THROW_ON_ERROR);
    } catch (JsonException $e) {
        jsonResponse(['success' => false, 'error' => 'Ungültige JSON-Eingabe'], 400);
    }

    $readings = $input['readings'] ?? [];
    if (!is_array($readings) || empty($readings)) {
        jsonResponse(['success' => false, 'error' => 'Keine Ablesungen übergeben'], 400);
    }

    // Max. 500 Einträge pro Batch
    if (count($readings) > 500) {
        jsonResponse(['success' => false, 'error' => 'Zu viele Einträge (max. 500 pro Batch)'], 400);
    }

    $userId    = (int)$_SESSION['id'];
    $projectId = (int)($_SESSION['active_project_id'] ?? 1);
    $results   = [];
    $saved     = 0;
    $skipped   = 0;
    $errors    = 0;

    foreach ($readings as $idx => $reading) {
        $meterId      = isset($reading['meter_id'])     ? (int)$reading['meter_id']      : null;
        $readingValue = isset($reading['reading_value']) ? (float)$reading['reading_value'] : null;
        $readingDate  = trim($reading['reading_date']  ?? '');
        $localId      = !empty($reading['local_id'])    ? trim(substr($reading['local_id'], 0, 64)) : null;

        if (!$meterId || $readingValue === null || !$readingDate) {
            $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'error', 'message' => 'Pflichtfelder fehlen'];
            $errors++;
            continue;
        }

        // Datum prüfen
        $parsedDate = \DateTime::createFromFormat('Y-m-d', $readingDate);
        if (!$parsedDate || $parsedDate->format('Y-m-d') !== $readingDate) {
            $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'error', 'message' => 'Ungültiges Datum'];
            $errors++;
            continue;
        }

        try {
            // Deduplizierung
            if ($localId) {
                $existing = $database->getExplorer()->table('mm_meter_readings')
                    ->where('local_id', $localId)
                    ->fetch();
                if ($existing) {
                    $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'duplicate', 'reading_id' => (int)$existing->offsetGet('id')];
                    $skipped++;
                    continue;
                }
            }

            // Zähler-Zugriffsrecht
            $meter = $database->getExplorer()->table('mm_meter')
                ->where('id', $meterId)
                ->where('project_id', $projectId)
                ->fetch();
            if (!$meter) {
                $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'error', 'message' => 'Zähler nicht gefunden'];
                $errors++;
                continue;
            }

            $insertData = [
                'meter_id'      => $meterId,
                'project_id'    => $projectId,
                'reading_value' => $readingValue,
                'reading_date'  => $readingDate,
                'reading_time'  => !empty($reading['reading_time']) ? trim($reading['reading_time']) : null,
                'note'          => !empty($reading['note'])         ? trim(substr($reading['note'], 0, 1000)) : null,
                'created_by'    => $userId,
                'synced_at'     => date('Y-m-d H:i:s'),
                'created_at'    => date('Y-m-d H:i:s'),
            ];
            if ($localId) {
                $insertData['local_id'] = $localId;
            }

            $row = $database->getExplorer()->table('mm_meter_readings')->insert($insertData);

            // Letzten Ablesewert aktualisieren (nur wenn das Datum neuer ist)
            $lastDate = $meter->offsetGet('reading_date');
            if (!$lastDate || $readingDate >= $lastDate) {
                $database->getExplorer()->table('mm_meter')
                    ->where('id', $meterId)
                    ->update(['reading_value' => $readingValue, 'reading_date' => $readingDate, 'updated_at' => date('Y-m-d H:i:s')]);
            }

            $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'saved', 'reading_id' => (int)$row->id];
            $saved++;
        } catch (Exception $e) {
            $log->error('meter_batch_sync Fehler bei Index ' . $idx . ': ' . $e->getMessage());
            $results[] = ['index' => $idx, 'local_id' => $localId, 'status' => 'error', 'message' => 'DB-Fehler'];
            $errors++;
        }
    }

    $log->info('meter_batch_sync abgeschlossen', ['saved' => $saved, 'skipped' => $skipped, 'errors' => $errors, 'user_id' => $userId]);

    jsonResponse([
        'success' => true,
        'summary' => ['saved' => $saved, 'skipped' => $skipped, 'errors' => $errors],
        'results' => $results,
    ]);
}

/**
 * Gibt die Ablesungshistorie eines einzelnen Zählers zurück.
 *
 * GET api.php?action=meter_readings&meter_id=5[&limit=50][&offset=0]
 */
#[NoReturn] function handleMeterReadings($database, $log): void
{
    if (!isset($_SESSION['id']) || empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    $meterId   = isset($_GET['meter_id']) ? (int)$_GET['meter_id'] : null;
    $limit     = min((int)($_GET['limit']  ?? 50), 500);
    $offset    = max((int)($_GET['offset'] ?? 0),  0);
    $projectId = (int)($_SESSION['active_project_id'] ?? 1);

    if (!$meterId) {
        jsonResponse(['success' => false, 'error' => 'meter_id fehlt'], 400);
    }

    try {
        // Zähler prüfen
        $meter = $database->getExplorer()->table('mm_meter')
            ->where('id', $meterId)
            ->where('project_id', $projectId)
            ->fetch();

        if (!$meter) {
            jsonResponse(['success' => false, 'error' => 'Zähler nicht gefunden'], 404);
        }

        $rows = $database->getExplorer()->table('mm_meter_readings')
            ->where('meter_id', $meterId)
            ->where('project_id', $projectId)
            ->order('reading_date DESC, reading_time DESC')
            ->limit($limit, $offset)
            ->fetchAll();

        $total = $database->getExplorer()->table('mm_meter_readings')
            ->where('meter_id', $meterId)
            ->where('project_id', $projectId)
            ->count('*');

        $readings = [];
        foreach ($rows as $r) {
            $readings[] = [
                'id'            => (int)$r->offsetGet('id'),
                'local_id'      => $r->offsetGet('local_id'),
                'reading_value' => (float)$r->offsetGet('reading_value'),
                'reading_date'  => $r->offsetGet('reading_date'),
                'reading_time'  => $r->offsetGet('reading_time'),
                'note'          => $r->offsetGet('note'),
                'image_path'    => $r->offsetGet('image_path'),
                'created_by'    => $r->offsetGet('created_by') ? (int)$r->offsetGet('created_by') : null,
                'synced_at'     => $r->offsetGet('synced_at'),
                'created_at'    => $r->offsetGet('created_at'),
            ];
        }

        jsonResponse([
            'success'  => true,
            'meter_id' => $meterId,
            'total'    => (int)$total,
            'limit'    => $limit,
            'offset'   => $offset,
            'readings' => $readings,
        ]);
    } catch (Exception $e) {
        $log->error('meter_readings Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/** Maximale Baum-Tiefe für Topologie-Rekursion (Zirkelschutz) */
const TOPOLOGY_MAX_DEPTH = 20;

/**
 * GET api.php?action=meter_topology[&project_id=X][&meter_type=water|power|heating]
 *
 * Gibt die vollständige Zähler-Hierarchie als Baumstruktur zurück.
 * Für Typ-Filter: &meter_type=water|power|heating
 */
#[NoReturn] function handleMeterTopology(Database $database, Logger $log): void
{
    if (empty($_SESSION['id'])) {
        jsonResponse(['success' => false, 'error' => 'Nicht authentifiziert'], 401);
    }

    try {
        $projectId = (int)(
            $_GET['project_id'] ??
            $_SESSION['active_project_id'] ??
            $_SESSION['project_id'] ??
            1
        );

        $filterType = $_GET['meter_type'] ?? null;
        if ($filterType && !in_array($filterType, ['water', 'power', 'heating'], true)) {
            $filterType = null;
        }

        // Zähler laden
        $meterQuery = $database->getExplorer()
            ->table('mm_meter')
            ->where('project_id', $projectId)
            ->where('is_active', 1)
            ->order('meter_type ASC, name ASC, meter_number ASC');
        if ($filterType) {
            $meterQuery->where('meter_type', $filterType);
        }

        $metersById = [];
        $allMeters  = [];
        foreach ($meterQuery->fetchAll() as $row) {
            $typeLabel = match ($row->offsetGet('meter_type')) {
                'water'   => 'Wasser',
                'power'   => 'Strom',
                'heating' => 'Fernwärme',
                default   => 'Unbekannt',
            };
            $m = [
                'id'            => (int)$row->offsetGet('id'),
                'name'          => $row->offsetGet('name'),
                'meter_number'  => $row->offsetGet('meter_number'),
                'meter_type'    => $row->offsetGet('meter_type'),
                'unit'          => $row->offsetGet('unit'),
                'location'      => $row->offsetGet('location'),
                'building_id'   => (int)$row->offsetGet('building_id'),
                'reading_value' => $row->offsetGet('reading_value') !== null
                    ? (float)$row->offsetGet('reading_value') : null,
                'reading_date'  => $row->offsetGet('reading_date')
                    ? (string)$row->offsetGet('reading_date') : null,
                'type_label'    => $typeLabel,
            ];
            $metersById[$m['id']] = $m;
            $allMeters[] = $m;
        }

        // Verknüpfungen laden
        $linkQuery = $database->getExplorer()
            ->table('mm_meter_topology')
            ->where('project_id', $projectId)
            ->order('meter_type ASC, sort_order ASC, id ASC');
        if ($filterType) {
            $linkQuery->where('meter_type', $filterType);
        }

        $links = [];
        $childIds = [];
        foreach ($linkQuery->fetchAll() as $row) {
            $links[] = [
                'id'              => (int)$row->offsetGet('id'),
                'parent_meter_id' => (int)$row->offsetGet('parent_meter_id'),
                'child_meter_id'  => (int)$row->offsetGet('child_meter_id'),
                'meter_type'      => $row->offsetGet('meter_type'),
                'sort_order'      => (int)$row->offsetGet('sort_order'),
            ];
            $childIds[(int)$row->offsetGet('child_meter_id')] = true;
        }

        // Bäume aufbauen
        $trees = ['water' => [], 'power' => [], 'heating' => []];
        foreach (['water', 'power', 'heating'] as $type) {
            if ($filterType && $filterType !== $type) {
                continue;
            }
            foreach ($allMeters as $meter) {
                if ($meter['meter_type'] !== $type) {
                    continue;
                }
                if (!isset($childIds[$meter['id']])) {
                    $trees[$type][] = buildApiTopologyTree($metersById, $links, $meter['id']);
                }
            }
        }

        jsonResponse([
            'success'    => true,
            'trees'      => $trees,
            'project_id' => $projectId,
            'meter_type' => $filterType,
        ]);

    } catch (Exception $e) {
        $log->error('meter_topology Fehler: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * Hilfsfunktion: Baut rekursiv einen Baum-Knoten auf (für handleMeterTopology).
 */
function buildApiTopologyTree(array $metersById, array $links, int $meterId, int $depth = 0): array
{
    if ($depth > TOPOLOGY_MAX_DEPTH || !isset($metersById[$meterId])) {
        return [];
    }

    $meter    = $metersById[$meterId];
    $children = [];

    foreach ($links as $link) {
        if ((int)$link['parent_meter_id'] === $meterId) {
            $child = buildApiTopologyTree($metersById, $links, (int)$link['child_meter_id'], $depth + 1);
            if (!empty($child)) {
                $child['link_id']    = $link['id'];
                $child['sort_order'] = $link['sort_order'];
                $children[]          = $child;
            }
        }
    }

    usort($children, static fn($a, $b) => ($a['sort_order'] ?? 0) <=> ($b['sort_order'] ?? 0));

    return [
        'id'            => $meter['id'],
        'name'          => $meter['name'],
        'meter_number'  => $meter['meter_number'],
        'meter_type'    => $meter['meter_type'],
        'type_label'    => $meter['type_label'],
        'unit'          => $meter['unit'],
        'location'      => $meter['location'],
        'reading_value' => $meter['reading_value'],
        'reading_date'  => $meter['reading_date'],
        'children'      => $children,
    ];
}

// ============================================================================
// BENUTZER-API – Authentifizierung & Token-Verwaltung
// Alle Endpunkte geben JSON zurück.
// ============================================================================

/**
 * Hilfsfunktion: Gibt Benutzer-Daten und Berechtigungen aus der DB zurück.
 * Liest $_SESSION['id'] (gesetzt durch Browser-Session ODER validierten User-API-Token).
 *
 * @return array|null Assoziatives Array mit id, username, vname, nname, email, is_admin, permissions
 *                    oder null wenn nicht authentifiziert
 */
function getApiUser(Database $db): ?array
{
    if (empty($_SESSION['id'])) {
        return null;
    }
    $userId = (int)$_SESSION['id'];
    $user = $db->getExplorer()->table('users')->where('id', $userId)->fetch();
    if (!$user) {
        return null;
    }
    $permissions = json_decode((string)($user->offsetGet('permissions') ?? '{}'), true) ?? [];
    $isAdmin = !empty($permissions['admin']);
    return [
        'id'          => $userId,
        'username'    => $user->offsetGet('username'),
        'vname'       => $user->offsetGet('vname'),
        'nname'       => $user->offsetGet('nname'),
        'email'       => $user->offsetGet('email'),
        'permissions' => $permissions,
        'is_admin'    => $isAdmin,
    ];
}

/**
 * Prüft eine Berechtigung für den API-Benutzer.
 * Gibt true zurück wenn admin ODER die spezifische Berechtigung gesetzt ist.
 */
function apiUserHasPermission(array $apiUser, string $permission): bool
{
    return !empty($apiUser['is_admin']) || !empty($apiUser['permissions'][$permission]);
}

/**
 * Liest und validiert den project_id Query-Parameter.
 * Für Nicht-Admins wird geprüft, ob der Benutzer dem angeforderten Projekt zugeordnet ist.
 * Gibt bei fehlendem Zugriff eine 403-Antwort aus.
 *
 * @return int Validierte Projekt-ID
 */
function getValidatedProjectId(array $apiUser, Database $db): int
{
    $defaultProjectId = (int)($_SESSION['active_project_id'] ?? 1);
    if (!isset($_GET['project_id'])) {
        return $defaultProjectId;
    }
    $requestedId = (int)$_GET['project_id'];
    if ($apiUser['is_admin']) {
        return $requestedId;
    }
    if (!ProjectContext::getInstance($db)->canUserAccessProject($apiUser['id'], $requestedId)) {
        jsonResponse(['success' => false, 'error' => 'Kein Zugriff auf dieses Projekt'], 403);
    }
    return $requestedId;
}

/**
 * POST api.php?action=auth_login
 * Body JSON oder POST-Felder: { "username": "...", "password": "...",
 *                               "token_name": "Android App", "ttl_days": 30 }
 *
 * Erstellt einen persönlichen User-API-Token (dkc_...) der in nachfolgenden
 * Anfragen als Authorization-Bearer-Token verwendet werden kann.
 * Öffentlicher Endpunkt – kein vorheriges Auth erforderlich.
 */
#[NoReturn] function handleAuthLogin(Database $db, Logger $log, RemoteAddress $radd): void
{
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        jsonResponse(['success' => false, 'error' => 'Method not allowed. Use POST.'], 405);
    }

    $raw   = (string)(file_get_contents('php://input') ?: '');
    $input = json_decode($raw, true) ?? [];

    $username  = trim($input['username']   ?? ($_POST['username']   ?? ''));
    $password  = $input['password']        ?? ($_POST['password']   ?? '');
    $tokenName = mb_substr(trim($input['token_name'] ?? ($_POST['token_name'] ?? 'API Token')), 0, 255);
    $ttlDays   = max(0, min(3650, (int)($input['ttl_days'] ?? ($_POST['ttl_days'] ?? 30))));

    if (!$username || !$password) {
        jsonResponse(['success' => false, 'error' => 'username und password sind erforderlich'], 400);
    }

    // Brute-Force-Schutz
    try {
        $bfp   = \system\helper\BruteForceProtection::getInstance($db);
        $rlCheck = $bfp->isRequestAllowed($radd->getClientIP(), 'api_login');
        if (!$rlCheck['allowed']) {
            $log->warning('auth_login: rate limit exceeded', ['ip' => $radd->getClientIP()]);
            http_response_code(429);
            header('Retry-After: 60');
            jsonResponse(['success' => false, 'error' => $rlCheck['message'] ?? 'Too many requests'], 429);
        }
    } catch (Exception $e) {
        $log->warning('auth_login: BruteForce check failed: ' . $e->getMessage());
    }

    // Benutzer suchen (Username oder Alias)
    $userRow = $db->getExplorer()->table('users')
        ->where('username = ? OR alias = ?', $username, $username)
        ->fetch();

    if (!$userRow || !password_verify($password, (string)$userRow->offsetGet('passwd'))) {
        if (isset($bfp)) {
            $bfp->recordLoginAttempt($username, $radd->getClientIP(), false);
        }
        $log->warning('auth_login: invalid credentials', ['username' => $username, 'ip' => $radd->getClientIP()]);
        // Constant-time response – vermeidet User-Enumeration-Timing
        usleep(random_int(150_000, 300_000));
        jsonResponse(['success' => false, 'error' => 'Ungültige Anmeldedaten'], 401);
    }

    if (isset($bfp)) {
        $bfp->recordLoginAttempt($username, $radd->getClientIP(), true);
        $bfp->resetFailedAttempts($username, $radd->getClientIP());
    }

    // Persönlichen User-API-Token generieren (dkc_ + 64 Hex-Zeichen = 256 Bit Entropie)
    try {
        $token = 'dkc_' . bin2hex(random_bytes(32));
    } catch (\Random\RandomException $e) {
        $log->error('auth_login: random_bytes failed: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Token-Generierung fehlgeschlagen'], 500);
    }

    $expiresAt = $ttlDays > 0
        ? (new \DateTime())->modify("+{$ttlDays} days")->format('Y-m-d H:i:s')
        : null;

    // Vorhandene Tokens mit gleichem Namen für diesen User löschen (kein Token-Müll)
    try {
        $db->getExplorer()->table('user_api_tokens')
            ->where('user_id = ? AND name = ?', $userRow->offsetGet('id'), $tokenName)
            ->delete();
    } catch (Exception $e) {
        $log->warning('auth_login: could not delete old tokens: ' . $e->getMessage());
    }

    try {
        $db->getExplorer()->table('user_api_tokens')->insert([
            'user_id'      => $userRow->offsetGet('id'),
            'token'        => hash('sha256', $token), // SHA-256-Hash speichern; Klartext nur an Client zurückgeben
            'name'         => $tokenName,
            'expires_at'   => $expiresAt,
            'last_ip'      => $radd->getClientIP(),
            'last_used_at' => date('Y-m-d H:i:s'),
            'created_at'   => date('Y-m-d H:i:s'),
        ]);
    } catch (Exception $e) {
        $log->error('auth_login: token insert failed: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Token konnte nicht gespeichert werden. Migration ausgeführt?'], 500);
    }

    $permissions = json_decode((string)($userRow->offsetGet('permissions') ?? '{}'), true) ?? [];

    $log->info('auth_login: new user API token created', [
        'user_id'    => $userRow->offsetGet('id'),
        'token_name' => $tokenName,
        'ip'         => $radd->getClientIP(),
    ]);

    jsonResponse([
        'success'    => true,
        'token'      => $token,
        'token_type' => 'Bearer',
        'expires_at' => $expiresAt,
        'user'       => [
            'id'       => (int)$userRow->offsetGet('id'),
            'username' => $userRow->offsetGet('username'),
            'vname'    => $userRow->offsetGet('vname'),
            'nname'    => $userRow->offsetGet('nname'),
            'email'    => $userRow->offsetGet('email'),
            'is_admin' => !empty($permissions['admin']),
        ],
    ]);
}

/**
 * POST api.php?action=auth_logout
 * Authorization: Bearer dkc_...
 *
 * Invalidiert den aktuellen User-API-Token.
 */
#[NoReturn] function handleAuthLogout(Database $db, Logger $log, RemoteAddress $radd): void
{
    $token = null;
    $hdr   = getallheaders();
    if (isset($hdr['Authorization']) && preg_match('/^Bearer\s+(dkc_\S+)$/i', $hdr['Authorization'], $m)) {
        $token = trim($m[1]);
    }

    if ($token) {
        try {
            $deleted = $db->getExplorer()->table('user_api_tokens')->where('token', hash('sha256', $token))->delete();
            $log->info('auth_logout: token invalidated', ['rows' => $deleted, 'ip' => $radd->getClientIP()]);
        } catch (Exception $e) {
            $log->error('auth_logout: delete failed: ' . $e->getMessage());
        }
    }

    jsonResponse(['success' => true, 'message' => 'Erfolgreich abgemeldet']);
}

/**
 * GET api.php?action=auth_status
 * Authorization: Bearer dkc_...  (oder aktive Session)
 *
 * Gibt an ob der Token / die Session gültig sind und gibt Benutzer-Infos zurück.
 */
#[NoReturn] function handleAuthStatus(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'authenticated' => false, 'error' => 'Not authenticated'], 401);
    }
    jsonResponse([
        'success'       => true,
        'authenticated' => true,
        'user'          => [
            'id'       => $apiUser['id'],
            'username' => $apiUser['username'],
            'vname'    => $apiUser['vname'],
            'nname'    => $apiUser['nname'],
            'email'    => $apiUser['email'],
            'is_admin' => $apiUser['is_admin'],
        ],
    ]);
}

/**
 * GET api.php?action=user_info
 * Gibt erweiterte Benutzer-Informationen inkl. Berechtigungen zurück.
 */
#[NoReturn] function handleUserInfo(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'error' => 'Not authenticated'], 401);
    }

    // Aktives Projekt
    $projectId = (int)($_SESSION['active_project_id'] ?? 1);

    jsonResponse([
        'success'    => true,
        'user'       => [
            'id'               => $apiUser['id'],
            'username'         => $apiUser['username'],
            'vname'            => $apiUser['vname'],
            'nname'            => $apiUser['nname'],
            'email'            => $apiUser['email'],
            'is_admin'         => $apiUser['is_admin'],
            'active_project_id'=> $projectId,
        ],
        'permissions' => $apiUser['permissions'],
    ]);
}

/**
 * GET api.php?action=user_tokens_list
 * Gibt alle eigenen User-API-Tokens zurück (ohne den Token-Wert selbst).
 */
#[NoReturn] function handleUserTokensList(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'error' => 'Not authenticated'], 401);
    }

    try {
        $rows = $db->getExplorer()->table('user_api_tokens')
            ->where('user_id', $apiUser['id'])
            ->order('created_at DESC')
            ->fetchAll();

        $tokens = [];
        foreach ($rows as $row) {
            $tokens[] = [
                'id'           => (int)$row->offsetGet('id'),
                'name'         => $row->offsetGet('name'),
                'expires_at'   => $row->offsetGet('expires_at'),
                'last_used_at' => $row->offsetGet('last_used_at'),
                'last_ip'      => $row->offsetGet('last_ip'),
                'created_at'   => $row->offsetGet('created_at'),
            ];
        }
        jsonResponse(['success' => true, 'tokens' => $tokens]);
    } catch (Exception $e) {
        $log->error('user_tokens_list: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * DELETE/POST api.php?action=user_token_delete
 * Body JSON: { "token_id": 5 }
 * Löscht einen eigenen Token anhand der Token-ID.
 */
#[NoReturn] function handleUserTokenDelete(Database $db, Logger $log, RemoteAddress $radd): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'error' => 'Not authenticated'], 401);
    }

    $raw     = (string)(file_get_contents('php://input') ?: '');
    $input   = json_decode($raw, true) ?? [];
    $tokenId = (int)($input['token_id'] ?? $_POST['token_id'] ?? 0);

    if (!$tokenId) {
        jsonResponse(['success' => false, 'error' => 'token_id fehlt'], 400);
    }

    try {
        $deleted = $db->getExplorer()->table('user_api_tokens')
            ->where('id', $tokenId)
            ->where('user_id', $apiUser['id'])  // Sicherheits-Check: nur eigene Tokens
            ->delete();

        if (!$deleted) {
            jsonResponse(['success' => false, 'error' => 'Token nicht gefunden oder keine Berechtigung'], 404);
        }

        $log->info('user_token_delete: token deleted', ['token_id' => $tokenId, 'user_id' => $apiUser['id'], 'ip' => $radd->getClientIP()]);
        jsonResponse(['success' => true, 'message' => 'Token gelöscht']);
    } catch (Exception $e) {
        $log->error('user_token_delete: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// NEA – Netzersatzanlagen API
// ============================================================================

/**
 * GET api.php?action=nea_systems[&project_id=X]
 * Berechtigung: nea_view oder admin
 */
#[NoReturn] function handleNeaSystems(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'nea_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (nea_view)'], 403);
    }

    $projectId = getValidatedProjectId($apiUser, $db);

    try {
        $rows = $db->getExplorer()->table('nea_systems')
            ->where('project_id', $projectId)
            ->order('name ASC')
            ->fetchAll();

        $systems = [];
        foreach ($rows as $row) {
            // Letzte Inspektion bestimmen
            $lastInsp = $db->getExplorer()->table('nea_inspections')
                ->where('nea_system_id', $row->offsetGet('id'))
                ->where('status', 'completed')
                ->order('inspection_date DESC')
                ->fetch();

            $systems[] = [
                'id'              => (int)$row->offsetGet('id'),
                'name'            => $row->offsetGet('name'),
                'description'     => $row->offsetGet('notes'), // notes als description
                'location'        => $row->offsetGet('location'),
                'manufacturer'    => $row->offsetGet('manufacturer'),
                'model'           => $row->offsetGet('model'),
                'serial_number'   => $row->offsetGet('serial_number'),
                'installation_date'=> $row->offsetGet('installation_date'),
                'enabled'         => (bool)$row->offsetGet('enabled'),
                'project_id'      => (int)$row->offsetGet('project_id'),
                'last_inspection_date'   => $lastInsp ? $lastInsp->offsetGet('inspection_date') : null,
                'last_inspection_result' => $lastInsp ? $lastInsp->offsetGet('overall_result') : null,
            ];
        }

        jsonResponse(['success' => true, 'project_id' => $projectId, 'systems' => $systems]);
    } catch (Exception $e) {
        $log->error('nea_systems: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=nea_inspections[&system_id=X&year=YYYY&status=S&limit=N&offset=M]
 * Berechtigung: nea_view oder admin
 */
#[NoReturn] function handleNeaInspections(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'nea_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (nea_view)'], 403);
    }

    $projectId = (int)($_SESSION['active_project_id'] ?? 1);
    $systemId  = isset($_GET['system_id']) ? (int)$_GET['system_id'] : null;
    $year      = isset($_GET['year'])      ? (int)$_GET['year']      : null;
    $status    = $_GET['status']           ?? null;
    $limit     = min((int)($_GET['limit']  ?? 50), 200);
    $offset    = max((int)($_GET['offset'] ?? 0), 0);

    $allowedStatuses = ['in_progress', 'completed', 'failed', 'cancelled'];
    if ($status && !in_array($status, $allowedStatuses, true)) {
        jsonResponse(['success' => false, 'error' => 'Ungültiger status-Wert'], 400);
    }

    try {
        $query = $db->getExplorer()->table('nea_inspections')
            ->where('project_id', $projectId);

        if ($systemId) $query->where('nea_system_id', $systemId);
        if ($year)     $query->where('YEAR(inspection_date) = ?', $year);
        if ($status)   $query->where('status', $status);

        $total = $query->count('*');

        $rows = $query->order('inspection_date DESC')->limit($limit, $offset)->fetchAll();

        $inspections = [];
        foreach ($rows as $row) {
            $system = $db->getExplorer()->table('nea_systems')->get($row->offsetGet('nea_system_id'));
            $inspections[] = [
                'id'              => (int)$row->offsetGet('id'),
                'nea_system_id'   => (int)$row->offsetGet('nea_system_id'),
                'system_name'     => $system ? $system->offsetGet('name') : null,
                'inspection_type' => $row->offsetGet('inspection_type'),
                'inspection_date' => $row->offsetGet('inspection_date'),
                'inspector_name'  => $row->offsetGet('inspector_name'),
                'status'          => $row->offsetGet('status'),
                'overall_result'  => $row->offsetGet('overall_result'),
                'runtime_hours'   => $row->offsetGet('runtime_hours'),
                'notes'           => $row->offsetGet('notes'),
                'created_at'      => $row->offsetGet('created_at'),
            ];
        }

        jsonResponse([
            'success'      => true,
            'project_id'   => $projectId,
            'total'        => (int)$total,
            'limit'        => $limit,
            'offset'       => $offset,
            'inspections'  => $inspections,
        ]);
    } catch (Exception $e) {
        $log->error('nea_inspections: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=nea_inspection_detail&id=X
 * Berechtigung: nea_view oder admin
 */
#[NoReturn] function handleNeaInspectionDetail(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'nea_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (nea_view)'], 403);
    }

    $id = (int)($_GET['id'] ?? 0);
    if (!$id) {
        jsonResponse(['success' => false, 'error' => 'Parameter id fehlt'], 400);
    }

    try {
        $inspection = $db->getExplorer()->table('nea_inspections')->get($id);
        if (!$inspection) {
            jsonResponse(['success' => false, 'error' => 'Prüfung nicht gefunden'], 404);
        }

        $system        = $db->getExplorer()->table('nea_systems')->get($inspection->offsetGet('nea_system_id'));
        $checklistData = $inspection->offsetGet('checklist_data')
            ? (json_decode($inspection->offsetGet('checklist_data'), true) ?? [])
            : [];
        $defectNotes   = $inspection->offsetGet('defect_notes')
            ? (json_decode($inspection->offsetGet('defect_notes'), true) ?? [])
            : [];
        $photos        = $inspection->offsetGet('photos')
            ? (json_decode($inspection->offsetGet('photos'), true) ?? [])
            : [];

        jsonResponse([
            'success'    => true,
            'inspection' => [
                'id'               => (int)$inspection->offsetGet('id'),
                'nea_system_id'    => (int)$inspection->offsetGet('nea_system_id'),
                'system'           => $system ? [
                    'id'   => (int)$system->offsetGet('id'),
                    'name' => $system->offsetGet('name'),
                ] : null,
                'inspection_type'  => $inspection->offsetGet('inspection_type'),
                'inspection_date'  => $inspection->offsetGet('inspection_date'),
                'inspector_id'     => $inspection->offsetGet('inspector_id'),
                'inspector_name'   => $inspection->offsetGet('inspector_name'),
                'status'           => $inspection->offsetGet('status'),
                'overall_result'   => $inspection->offsetGet('overall_result'),
                'runtime_hours'    => $inspection->offsetGet('runtime_hours'),
                'runtime_hours_after' => $inspection->offsetGet('runtime_hours_after'),
                'defects_found'    => $inspection->offsetGet('defects_found'),
                'corrective_actions'=> $inspection->offsetGet('corrective_actions'),
                'notes'            => $inspection->offsetGet('notes'),
                'checklist_data'   => $checklistData,
                'defect_notes'     => $defectNotes,
                'photos'           => $photos,
                'created_at'       => $inspection->offsetGet('created_at'),
            ],
        ]);
    } catch (Exception $e) {
        $log->error('nea_inspection_detail: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=nea_dashboard[&project_id=X]
 * Berechtigung: nea_view oder admin
 */
#[NoReturn] function handleNeaDashboard(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'nea_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (nea_view)'], 403);
    }

    $projectId = getValidatedProjectId($apiUser, $db);

    try {
        $stats = [
            'total_systems'           => $db->getExplorer()->table('nea_systems')->where('project_id', $projectId)->where('enabled', 1)->count(),
            'inspections_this_week'   => $db->getExplorer()->table('nea_inspections')
                ->where('project_id', $projectId)
                ->where('inspection_date >= ?', date('Y-m-d', strtotime('monday this week')))
                ->count(),
            'inspections_this_month'  => $db->getExplorer()->table('nea_inspections')
                ->where('project_id', $projectId)
                ->where('MONTH(inspection_date) = ?', date('m'))
                ->where('YEAR(inspection_date) = ?', date('Y'))
                ->count(),
            'failed_last_30_days'     => $db->getExplorer()->table('nea_inspections')
                ->where('project_id', $projectId)
                ->where('overall_result', 'failed')
                ->where('inspection_date >= ?', date('Y-m-d', strtotime('-30 days')))
                ->count(),
        ];

        // Anlagen die > 7 Tage nicht geprüft wurden
        $systems  = $db->getExplorer()->table('nea_systems')->where('project_id', $projectId)->where('enabled', 1)->fetchAll();
        $dueTests = [];
        foreach ($systems as $sys) {
            $lastInsp = $db->getExplorer()->table('nea_inspections')
                ->where('nea_system_id', $sys->offsetGet('id'))
                ->where('status', 'completed')
                ->order('inspection_date DESC')
                ->fetch();

            $daysSince = $lastInsp
                ? (time() - strtotime((string)$lastInsp->offsetGet('inspection_date'))) / 86400
                : 999;

            if ($daysSince >= 7) {
                $dueTests[] = [
                    'system_id'        => (int)$sys->offsetGet('id'),
                    'system_name'      => $sys->offsetGet('name'),
                    'days_overdue'     => (int)floor($daysSince - 7),
                    'last_inspection'  => $lastInsp ? $lastInsp->offsetGet('inspection_date') : null,
                ];
            }
        }

        // Letzte 10 Inspektionen
        $recentRows = $db->getExplorer()->table('nea_inspections')
            ->where('project_id', $projectId)
            ->order('inspection_date DESC')
            ->limit(10)
            ->fetchAll();
        $recentInspections = [];
        foreach ($recentRows as $r) {
            $recentInspections[] = [
                'id'             => (int)$r->offsetGet('id'),
                'nea_system_id'  => (int)$r->offsetGet('nea_system_id'),
                'inspection_date'=> $r->offsetGet('inspection_date'),
                'inspector_name' => $r->offsetGet('inspector_name'),
                'status'         => $r->offsetGet('status'),
                'overall_result' => $r->offsetGet('overall_result'),
            ];
        }

        jsonResponse([
            'success'            => true,
            'project_id'         => $projectId,
            'stats'              => $stats,
            'due_tests'          => $dueTests,
            'recent_inspections' => $recentInspections,
        ]);
    } catch (Exception $e) {
        $log->error('nea_dashboard: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// MM – Mängelmeldungen API
// ============================================================================

/**
 * GET api.php?action=mm_list[&status=X&limit=N&offset=M&street=X]
 * Berechtigung: view_mm_list oder admin
 */
#[NoReturn] function handleMmList(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'view_mm_list')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (view_mm_list)'], 403);
    }

    $limit  = min((int)($_GET['limit']  ?? 50), 200);
    $offset = max((int)($_GET['offset'] ?? 0), 0);
    $status = isset($_GET['status']) ? (int)$_GET['status'] : null;
    $street = isset($_GET['street']) ? trim($_GET['street']) : null;

    try {
        if (!apiUserHasPermission($apiUser, 'view_mm_list_all')) {
            jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (view_mm_list_all)'], 403);
        }

        $query = $db->getExplorer()->table('mm_messages');

        if ($status !== null) {
            $query->where('status', $status);
        }
        if ($street) {
            $query->where('street', $street);
        }

        $total = $query->count('*');
        $rows  = $query->order('datetime DESC')->limit($limit, $offset)->fetchAll();

        $messages = [];
        foreach ($rows as $row) {
            $messages[] = [
                'uid'             => $row->offsetGet('uid'),
                'status'          => (int)$row->offsetGet('status'),
                'betreff'         => $row->offsetGet('betreff'),
                'street'          => $row->offsetGet('street'),
                'whg'             => $row->offsetGet('whg'),
                'melder'          => $row->offsetGet('melder'),
                'datetime'        => $row->offsetGet('datetime') instanceof \DateTime
                    ? $row->offsetGet('datetime')->format('Y-m-d H:i:s')
                    : $row->offsetGet('datetime'),
                'dringlichkeit'   => $row->offsetGet('dringlichkeit') ?? 'normal',
                'nachunternehmer' => $row->offsetGet('nachunternehmer'),
                'scanned'         => (bool)$row->offsetGet('scanned'),
                'zugeh'           => $row->offsetGet('zugeh'),
            ];
        }

        jsonResponse([
            'success'  => true,
            'total'    => (int)$total,
            'limit'    => $limit,
            'offset'   => $offset,
            'messages' => $messages,
        ]);
    } catch (Exception $e) {
        $log->error('mm_list: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=mm_detail&uid=XXX
 * Berechtigung: view_mm oder admin
 */
#[NoReturn] function handleMmDetail(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'view_mm')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (view_mm)'], 403);
    }

    $uid = trim($_GET['uid'] ?? '');
    if (!$uid) {
        jsonResponse(['success' => false, 'error' => 'Parameter uid fehlt'], 400);
    }

    try {
        $mm = $db->getExplorer()->table('mm_messages')->where('uid', $uid)->fetch();
        if (!$mm) {
            jsonResponse(['success' => false, 'error' => 'Meldung nicht gefunden'], 404);
        }

        jsonResponse([
            'success' => true,
            'message' => [
                'uid'              => $mm->offsetGet('uid'),
                'status'           => (int)$mm->offsetGet('status'),
                'betreff'          => $mm->offsetGet('betreff'),
                'meldung_massage'  => $mm->offsetGet('meldung_massage'),
                'apleona'          => $mm->offsetGet('apleona'),
                'folge'            => $mm->offsetGet('folge'),
                'street'           => $mm->offsetGet('street'),
                'whg'              => $mm->offsetGet('whg'),
                'melder'           => $mm->offsetGet('melder'),
                'tel'              => $mm->offsetGet('tel'),
                'email'            => $mm->offsetGet('email'),
                'datetime'         => $mm->offsetGet('datetime') instanceof \DateTime
                    ? $mm->offsetGet('datetime')->format('Y-m-d H:i:s')
                    : $mm->offsetGet('datetime'),
                'dringlichkeit'    => $mm->offsetGet('dringlichkeit') ?? 'normal',
                'nachunternehmer'  => $mm->offsetGet('nachunternehmer'),
                'ekpreis'          => $mm->offsetGet('ekpreis'),
                'klausel'          => (bool)$mm->offsetGet('klausel'),
                'zugeh'            => $mm->offsetGet('zugeh'),
                'scanned'          => (bool)$mm->offsetGet('scanned'),
                'zeit'             => $mm->offsetGet('zeit'),
                'planon'           => $mm->offsetGet('planon'),
                'instructions'     => $mm->offsetGet('instructions')
                    ? json_decode($mm->offsetGet('instructions'), true)
                    : [],
            ],
        ]);
    } catch (Exception $e) {
        $log->error('mm_detail: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// Gebäudebegehungen API
// ============================================================================

/**
 * GET api.php?action=building_list[&project_id=X]
 * Berechtigung: building_view oder admin
 */
#[NoReturn] function handleBuildingList(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'building_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (building_view)'], 403);
    }

    $projectId = getValidatedProjectId($apiUser, $db);

    try {
        $rows = $db->getExplorer()->table('bi_buildings')
            ->where('project_id', $projectId)
            ->order('name ASC')
            ->fetchAll();

        $buildings = [];
        foreach ($rows as $row) {
            $buildings[] = [
                'id'          => (int)$row->offsetGet('id'),
                'name'        => $row->offsetGet('name'),
                'address'     => $row->offsetGet('address'),
                'description' => $row->offsetGet('description'),
                'enabled'     => (bool)$row->offsetGet('enabled'),
                'project_id'  => (int)$row->offsetGet('project_id'),
            ];
        }

        jsonResponse(['success' => true, 'project_id' => $projectId, 'buildings' => $buildings]);
    } catch (Exception $e) {
        $log->error('building_list: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=building_inspections[&building_id=X&status=S&year=Y&limit=N&offset=M]
 * Berechtigung: building_view oder admin
 */
#[NoReturn] function handleBuildingInspections(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'building_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (building_view)'], 403);
    }

    $projectId  = (int)($_SESSION['active_project_id'] ?? 1);
    $buildingId = isset($_GET['building_id']) ? (int)$_GET['building_id'] : null;
    $status     = $_GET['status'] ?? null;
    $year       = isset($_GET['year']) ? (int)$_GET['year'] : null;
    $limit      = min((int)($_GET['limit']  ?? 50), 200);
    $offset     = max((int)($_GET['offset'] ?? 0), 0);

    $allowedStatuses = ['open', 'in_progress', 'completed'];
    if ($status && !in_array($status, $allowedStatuses, true)) {
        jsonResponse(['success' => false, 'error' => 'Ungültiger status-Wert'], 400);
    }

    try {
        $query = $db->getExplorer()->table('bi_inspections')
            ->where('project_id', $projectId);

        if ($buildingId) $query->where('building_id', $buildingId);
        if ($status)     $query->where('status', $status);
        if ($year)       $query->where('YEAR(inspection_date) = ?', $year);

        $total = $query->count('*');
        $rows  = $query->order('inspection_date DESC')->limit($limit, $offset)->fetchAll();

        $inspections = [];
        foreach ($rows as $row) {
            $building = $db->getExplorer()->table('bi_buildings')->get($row->offsetGet('building_id'));
            $inspections[] = [
                'id'               => (int)$row->offsetGet('id'),
                'building_id'      => (int)$row->offsetGet('building_id'),
                'building_name'    => $building ? $building->offsetGet('name') : null,
                'title'            => $row->offsetGet('title'),
                'inspection_date'  => $row->offsetGet('inspection_date') instanceof \DateTime
                    ? $row->offsetGet('inspection_date')->format('Y-m-d H:i:s')
                    : $row->offsetGet('inspection_date'),
                'status'           => $row->offsetGet('status'),
                'overall_result'   => $row->offsetGet('overall_result'),
                'created_by_name'  => $row->offsetGet('created_by_name'),
                'last_editor_name' => $row->offsetGet('last_editor_name'),
                'weather'          => $row->offsetGet('weather'),
                'attendees'        => $row->offsetGet('attendees'),
            ];
        }

        jsonResponse([
            'success'    => true,
            'project_id' => $projectId,
            'total'      => (int)$total,
            'limit'      => $limit,
            'offset'     => $offset,
            'inspections'=> $inspections,
        ]);
    } catch (Exception $e) {
        $log->error('building_inspections: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=building_inspection_detail&id=X
 * Berechtigung: building_view oder admin
 */
#[NoReturn] function handleBuildingInspectionDetail(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'building_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (building_view)'], 403);
    }

    $id = (int)($_GET['id'] ?? 0);
    if (!$id) {
        jsonResponse(['success' => false, 'error' => 'Parameter id fehlt'], 400);
    }

    try {
        $inspection = $db->getExplorer()->table('bi_inspections')->get($id);
        if (!$inspection) {
            jsonResponse(['success' => false, 'error' => 'Begehung nicht gefunden'], 404);
        }

        $building   = $db->getExplorer()->table('bi_buildings')->get($inspection->offsetGet('building_id'));
        $resultRows = $db->getExplorer()->table('bi_results')
            ->where('inspection_id', $id)
            ->fetchAll();

        // Alle benötigten Checkpoints in einem Query vorladen (N+1 vermeiden)
        $checkpointIds = [];
        foreach ($resultRows as $r) {
            $cpId = (int)$r->offsetGet('checkpoint_id');
            if ($cpId) {
                $checkpointIds[$cpId] = $cpId;
            }
        }
        $checkpointsById = [];
        if (!empty($checkpointIds)) {
            $cpRows = $db->getExplorer()->table('bi_checkpoints')
                ->where('id', array_values($checkpointIds))
                ->fetchAll();
            foreach ($cpRows as $cp) {
                $checkpointsById[(int)$cp->offsetGet('id')] = $cp;
            }
        }

        $results = [];
        foreach ($resultRows as $r) {
            $cpId = (int)$r->offsetGet('checkpoint_id');
            $cp   = $checkpointsById[$cpId] ?? null;
            $results[] = [
                'id'             => (int)$r->offsetGet('id'),
                'checkpoint_id'  => $cpId,
                'checkpoint_name'=> $cp ? $cp->offsetGet('name') : null,
                'status'         => $r->offsetGet('status'),
                'note'           => $r->offsetGet('note'),
                'comment'        => $r->offsetGet('comment'),
                'edited_by_name' => $r->offsetGet('edited_by_name'),
                'edited_at'      => $r->offsetGet('edited_at') instanceof \DateTime
                    ? $r->offsetGet('edited_at')->format('Y-m-d H:i:s')
                    : $r->offsetGet('edited_at'),
            ];
        }

        jsonResponse([
            'success'    => true,
            'inspection' => [
                'id'               => (int)$inspection->offsetGet('id'),
                'building_id'      => (int)$inspection->offsetGet('building_id'),
                'building_name'    => $building ? $building->offsetGet('name') : null,
                'title'            => $inspection->offsetGet('title'),
                'inspection_date'  => $inspection->offsetGet('inspection_date') instanceof \DateTime
                    ? $inspection->offsetGet('inspection_date')->format('Y-m-d H:i:s')
                    : $inspection->offsetGet('inspection_date'),
                'status'           => $inspection->offsetGet('status'),
                'overall_result'   => $inspection->offsetGet('overall_result'),
                'created_by_name'  => $inspection->offsetGet('created_by_name'),
                'last_editor_name' => $inspection->offsetGet('last_editor_name'),
                'weather'          => $inspection->offsetGet('weather'),
                'attendees'        => $inspection->offsetGet('attendees'),
                'general_notes'    => $inspection->offsetGet('general_notes'),
                'results'          => $results,
            ],
        ]);
    } catch (Exception $e) {
        $log->error('building_inspection_detail: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// Klima – Klimaanlage / Air Conditioner API
// ============================================================================

/**
 * GET api.php?action=klima_devices
 * Gibt die konfigurierten Klimageräte aus der Datenbank zurück.
 * Berechtigung: view_groups oder admin
 */
#[NoReturn] function handleKlimaDevices(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'view_groups')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (view_groups)'], 403);
    }

    try {
        $rows = $db->getExplorer()->table('mnetOverridden')
            ->order('sort ASC, address ASC')
            ->fetchAll();

        $devices = [];
        foreach ($rows as $row) {
            $devices[] = [
                'address'       => (int)$row->offsetGet('address'),
                'name'          => $row->offsetGet('name'),
                'group_id'      => $row->offsetGet('group_id') !== null ? (int)$row->offsetGet('group_id') : null,
                'enabled'       => (bool)$row->offsetGet('enabled'),
                'sort'          => (int)$row->offsetGet('sort'),
            ];
        }

        jsonResponse(['success' => true, 'devices' => $devices]);
    } catch (Exception $e) {
        $log->error('klima_devices: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=klima_status[&address=X]
 * Gibt den gespeicherten Betriebsmodus und Override-Informationen zurück.
 * Berechtigung: view_groups oder admin
 *
 * Hinweis: Echtzeit-Temperaturen und Live-Status erfordern die RMI/ClimaticAPI-
 * Hardwareverbindung, die in der stateless-API nicht verfügbar ist. Diese Endpoints
 * liefern die in der Datenbank gespeicherten Konfigurationsdaten.
 */
#[NoReturn] function handleKlimaStatus(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'view_groups')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (view_groups)'], 403);
    }

    $address = isset($_GET['address']) ? (int)$_GET['address'] : null;

    try {
        $query = $db->getExplorer()->table('mnetOverridden');
        if ($address) {
            $query->where('address', $address);
        }
        $rows = $query->order('sort ASC, address ASC')->fetchAll();

        // Alle benötigten mnetOperating-Einträge in einem Query vorladen (N+1 vermeiden)
        $groupIds = [];
        foreach ($rows as $row) {
            $gid = $row->offsetGet('group_id');
            if ($gid !== null) {
                $groupIds[(int)$gid] = (int)$gid;
            }
        }
        $operatingByGroupId = [];
        if (!empty($groupIds)) {
            $operatingRows = $db->getExplorer()->table('mnetOperating')
                ->where('group_id', array_values($groupIds))
                ->fetchAll();
            foreach ($operatingRows as $op) {
                $operatingByGroupId[(int)$op->offsetGet('group_id')] = $op;
            }
        }

        $statusList = [];
        foreach ($rows as $row) {
            $gid       = $row->offsetGet('group_id');
            $operating = ($gid !== null && isset($operatingByGroupId[(int)$gid]))
                ? $operatingByGroupId[(int)$gid]
                : null;

            $statusList[] = [
                'address'        => (int)$row->offsetGet('address'),
                'name'           => $row->offsetGet('name'),
                'enabled'        => (bool)$row->offsetGet('enabled'),
                'group_id'       => $gid !== null ? (int)$gid : null,
                'operating_mode' => $operating ? $operating->offsetGet('mode') : null,
                'note'           => 'Echtzeit-Status erfordert direkte RMI-Verbindung',
            ];
        }

        jsonResponse(['success' => true, 'devices' => $statusList]);
    } catch (Exception $e) {
        $log->error('klima_status: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// Schlüsselverwaltung API
// ============================================================================

/**
 * GET api.php?action=keys_inventory[&limit=N&offset=M&status=active|inactive]
 * Berechtigung: keys_view oder admin
 */
#[NoReturn] function handleKeysInventory(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'keys_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (keys_view)'], 403);
    }

    $limit  = min((int)($_GET['limit']  ?? 50), 200);
    $offset = max((int)($_GET['offset'] ?? 0), 0);
    $status = $_GET['status'] ?? 'active';

    try {
        $query = $db->getExplorer()->table('keys_inventory');
        if ($status === 'active')   $query->where('enabled', 1);
        if ($status === 'inactive') $query->where('enabled', 0);

        $total = $query->count('*');
        $rows  = $query->order('number ASC')->limit($limit, $offset)->fetchAll();

        $keys = [];
        foreach ($rows as $row) {
            $keys[] = [
                'id'          => (int)$row->offsetGet('id'),
                'number'      => $row->offsetGet('number'),
                'name'        => $row->offsetGet('name'),
                'description' => $row->offsetGet('description'),
                'type_id'     => $row->offsetGet('type_id') !== null ? (int)$row->offsetGet('type_id') : null,
                'cabinet_id'  => $row->offsetGet('cabinet_id') !== null ? (int)$row->offsetGet('cabinet_id') : null,
                'total_count' => (int)$row->offsetGet('total_count'),
                'enabled'     => (bool)$row->offsetGet('enabled'),
            ];
        }

        jsonResponse([
            'success' => true,
            'total'   => (int)$total,
            'limit'   => $limit,
            'offset'  => $offset,
            'keys'    => $keys,
        ]);
    } catch (Exception $e) {
        $log->error('keys_inventory: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=keys_issued[&limit=N&offset=M]
 * Gibt aktuell ausgegebene Schlüssel zurück.
 * Berechtigung: keys_view oder admin
 */
#[NoReturn] function handleKeysIssued(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser || !apiUserHasPermission($apiUser, 'keys_view')) {
        jsonResponse(['success' => false, 'error' => 'Keine Berechtigung (keys_view)'], 403);
    }

    $limit  = min((int)($_GET['limit']  ?? 50), 200);
    $offset = max((int)($_GET['offset'] ?? 0), 0);

    try {
        $query = $db->getExplorer()->table('keys_issued')
            ->where('returned_at IS NULL');  // Nur nicht zurückgegebene Schlüssel

        $total = $query->count('*');
        $rows  = $query->order('issued_at DESC')->limit($limit, $offset)->fetchAll();

        $issued = [];
        foreach ($rows as $row) {
            $key = $db->getExplorer()->table('keys_inventory')->get($row->offsetGet('key_id'));
            $issued[] = [
                'id'            => (int)$row->offsetGet('id'),
                'key_id'        => (int)$row->offsetGet('key_id'),
                'key_number'    => $key ? $key->offsetGet('number') : null,
                'key_name'      => $key ? $key->offsetGet('name') : null,
                'recipient_name'=> $row->offsetGet('recipient_name'),
                'issued_at'     => $row->offsetGet('issued_at') instanceof \DateTime
                    ? $row->offsetGet('issued_at')->format('Y-m-d H:i:s')
                    : $row->offsetGet('issued_at'),
                'issued_by'     => $row->offsetGet('issued_by'),
                'notes'         => $row->offsetGet('notes'),
            ];
        }

        jsonResponse([
            'success' => true,
            'total'   => (int)$total,
            'limit'   => $limit,
            'offset'  => $offset,
            'issued'  => $issued,
        ]);
    } catch (Exception $e) {
        $log->error('keys_issued: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ============================================================================
// Dashboard & Projekte API
// ============================================================================

/**
 * GET api.php?action=dashboard_data[&project_id=X]
 * Gibt eine Übersicht aller Systemmodule zurück.
 * Berechtigung: Jeder eingeloggte Benutzer (Daten werden permissions-basiert gefiltert)
 */
#[NoReturn] function handleDashboardData(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'error' => 'Not authenticated'], 401);
    }

    $projectId = getValidatedProjectId($apiUser, $db);

    $data = ['success' => true, 'project_id' => $projectId];

    try {
        // MM-Statistiken (wenn berechtigt)
        if (apiUserHasPermission($apiUser, 'view_mm_list')) {
            $data['mm'] = [
                'total'     => $db->getExplorer()->table('mm_messages')->count('*'),
                'pending'   => $db->getExplorer()->table('mm_messages')->where('status', 0)->count('*'),
                'approved'  => $db->getExplorer()->table('mm_messages')->where('status', 1)->count('*'),
                'completed' => $db->getExplorer()->table('mm_messages')->where('status', 2)->count('*'),
            ];
        }

        // NEA-Statistiken (wenn berechtigt)
        if (apiUserHasPermission($apiUser, 'nea_view')) {
            $data['nea'] = [
                'total_systems'          => $db->getExplorer()->table('nea_systems')->where('project_id', $projectId)->where('enabled', 1)->count(),
                'inspections_this_month' => $db->getExplorer()->table('nea_inspections')
                    ->where('project_id', $projectId)
                    ->where('MONTH(inspection_date) = ?', date('m'))
                    ->where('YEAR(inspection_date) = ?', date('Y'))
                    ->count(),
            ];
        }

        // Gebäudebegehungen-Statistiken (wenn berechtigt)
        if (apiUserHasPermission($apiUser, 'building_view')) {
            $data['building'] = [
                'open'        => $db->getExplorer()->table('bi_inspections')->where('project_id', $projectId)->where('status', 'open')->count(),
                'in_progress' => $db->getExplorer()->table('bi_inspections')->where('project_id', $projectId)->where('status', 'in_progress')->count(),
                'completed'   => $db->getExplorer()->table('bi_inspections')->where('project_id', $projectId)->where('status', 'completed')->count(),
            ];
        }

        // Schlüssel-Statistiken (wenn berechtigt)
        if (apiUserHasPermission($apiUser, 'keys_view')) {
            $data['keys'] = [
                'total_inventory' => $db->getExplorer()->table('keys_inventory')->where('enabled', 1)->count(),
                'currently_issued'=> $db->getExplorer()->table('keys_issued')->where('returned_at IS NULL')->count(),
            ];
        }

        // Ungelesene Benachrichtigungen
        $data['notifications'] = [
            'unread' => $db->getExplorer()->table('browser_notifications')
                ->where('user_id', $apiUser['id'])
                ->where('sent', 0)
                ->count('*'),
        ];

        jsonResponse($data);
    } catch (Exception $e) {
        $log->error('dashboard_data: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

/**
 * GET api.php?action=projects_list
 * Gibt alle verfügbaren Projekte zurück.
 * Berechtigung: Jeder eingeloggte Benutzer
 */
#[NoReturn] function handleProjectsList(Database $db, Logger $log): void
{
    $apiUser = getApiUser($db);
    if (!$apiUser) {
        jsonResponse(['success' => false, 'error' => 'Not authenticated'], 401);
    }

    try {
        $projects = [];
        if ($apiUser['is_admin']) {
            $dbRows = $db->getExplorer()->table('projects')->order('name ASC')->fetchAll();
            foreach ($dbRows as $row) {
                $createdAt = $row->offsetGet('created_at');
                $projects[] = [
                    'id'          => (int)$row->offsetGet('id'),
                    'name'        => $row->offsetGet('name'),
                    'description' => $row->offsetGet('description'),
                    'status'      => $row->offsetGet('status'),
                    'created_at'  => $createdAt instanceof \DateTime
                        ? $createdAt->format('Y-m-d H:i:s')
                        : $createdAt,
                ];
            }
        } else {
            // Nicht-Admins sehen nur die ihnen zugeordneten (aktiven) Projekte
            $ctx          = ProjectContext::getInstance($db);
            $userProjects = $ctx->getUserProjects($apiUser['id']);
            if (!empty($userProjects)) {
                $projectIds = array_column($userProjects, 'project_id');
                $dbRows     = $db->getExplorer()->table('projects')
                    ->where('id', $projectIds)
                    ->order('name ASC')
                    ->fetchAll();
                foreach ($dbRows as $row) {
                    $createdAt = $row->offsetGet('created_at');
                    $projects[] = [
                        'id'          => (int)$row->offsetGet('id'),
                        'name'        => $row->offsetGet('name'),
                        'description' => $row->offsetGet('description'),
                        'status'      => $row->offsetGet('status'),
                        'created_at'  => $createdAt instanceof \DateTime
                            ? $createdAt->format('Y-m-d H:i:s')
                            : $createdAt,
                    ];
                }
            }
        }

        $activeProjectId = (int)($_SESSION['active_project_id'] ?? 1);

        jsonResponse([
            'success'           => true,
            'active_project_id' => $activeProjectId,
            'projects'          => $projects,
        ]);
    } catch (Exception $e) {
        $log->error('projects_list: ' . $e->getMessage());
        jsonResponse(['success' => false, 'error' => 'Interner Fehler'], 500);
    }
}

// ── Ende Benutzer-API ────────────────────────────────────────────────────────
$actions = [
    'sync_download' => static fn() => handleSyncDownload($api_functions, $log),
    'sms' => static fn() => handleSMS($api_functions, $api_data, $log),
    'email' => static fn() => handleEmail($api_functions, $log),
    'gotify' => static fn() => handleGotify($api_functions, $log),
    'rmi' => static fn() => handleRMI($api_functions, $log),
    'webhook' => static fn() => handleWebhook($api_functions, $log, $database, $radd),
    'notifications' => static fn() => handleNotifications($database, $log),
    'get_notification_count' => static fn() => handleGetNotificationCount($database, $log),
    'client_cache_version' => static fn() => handleClientCacheVersion($log),
    'ckeditor_draft' => static fn() => handleCKEditorDraft($database),
    // Zählererfassung – PWA Offline-fähig
    'meter_list'        => static fn() => handleMeterList($database, $log),
    'meter_submit'      => static fn() => handleMeterSubmit($database, $log),
    'meter_batch_sync'  => static fn() => handleMeterBatchSync($database, $log),
    'meter_readings'    => static fn() => handleMeterReadings($database, $log),
    'meter_qr_list'     => static fn() => handleMeterQrList($database, $log),
    'meter_deactivate'  => static fn() => handleMeterDeactivate($database, $log),
    'meter_activate'    => static fn() => handleMeterActivate($database, $log),
    'meter_buildings'   => static fn() => handleMeterBuildings($database, $log),
    'meter_whg'         => static fn() => handleMeterWhg($database, $log),
    'meter_users'       => static fn() => handleMeterUsers($database, $log),
    'meter_topology'    => static fn() => handleMeterTopology($database, $log),
    'dropdown_data'     => static fn() => handleDropdownData($database, $log),
    // ── Benutzer-Authentifizierung (externe Apps) ──────────────────────────────
    'auth_login'        => static fn() => handleAuthLogin($database, $log, $radd),
    'auth_logout'       => static fn() => handleAuthLogout($database, $log, $radd),
    'auth_status'       => static fn() => handleAuthStatus($database, $log),
    'user_info'         => static fn() => handleUserInfo($database, $log),
    'user_tokens_list'  => static fn() => handleUserTokensList($database, $log),
    'user_token_delete' => static fn() => handleUserTokenDelete($database, $log, $radd),
    // ── NEA – Netzersatzanlagen ────────────────────────────────────────────────
    'nea_systems'            => static fn() => handleNeaSystems($database, $log),
    'nea_inspections'        => static fn() => handleNeaInspections($database, $log),
    'nea_inspection_detail'  => static fn() => handleNeaInspectionDetail($database, $log),
    'nea_dashboard'          => static fn() => handleNeaDashboard($database, $log),
    // ── MM – Mängelmeldungen ───────────────────────────────────────────────────
    'mm_list'           => static fn() => handleMmList($database, $log),
    'mm_detail'         => static fn() => handleMmDetail($database, $log),
    // ── Gebäudebegehungen ──────────────────────────────────────────────────────
    'building_list'              => static fn() => handleBuildingList($database, $log),
    'building_inspections'       => static fn() => handleBuildingInspections($database, $log),
    'building_inspection_detail' => static fn() => handleBuildingInspectionDetail($database, $log),
    // ── Klima – Klimaanlagen / Air Conditioner ────────────────────────────────
    'klima_devices' => static fn() => handleKlimaDevices($database, $log),
    'klima_status'  => static fn() => handleKlimaStatus($database, $log),
    // ── Schlüsselverwaltung ────────────────────────────────────────────────────
    'keys_inventory' => static fn() => handleKeysInventory($database, $log),
    'keys_issued'    => static fn() => handleKeysIssued($database, $log),
    // ── Dashboard & Projekte ───────────────────────────────────────────────────
    'dashboard_data' => static fn() => handleDashboardData($database, $log),
    'projects_list'  => static fn() => handleProjectsList($database, $log),
];

$action = strtolower(trim($_GET['action'] ?? 'sync'));
if (isset($actions[$action])) {
    $log->debug('Dispatching action', ['action' => $action]);
    $actions[$action]();
} else {
    $log->warning('Unknown API action requested', [
        'action'     => $action,
        'ip'         => $radd->getClientIP(),
        'user_agent' => $_SERVER['HTTP_USER_AGENT'] ?? 'unknown',
        'get'        => $_GET,
    ]);
    http_response_code(404);
    echo json_encode(['error' => 'Unknown action: ' . $action]);
}
// ============================================================================
// ██████╗ TWS-App REST API Handler-Funktionen
// ============================================================================
// Alle Endpunkte folgen dem TWS-App API-Format:
//   { "success": bool, "data": mixed, "error": string, "server_time": int }
// Auth: Authorization: Bearer dkc_... (user_api_tokens)  oder aktive Session
// ============================================================================

/**
 * Sendet eine TWS-kompatible JSON-Antwort und beendet die Ausführung.
 * @param array $data    Vollständige Antwort-Payload
 * @param int   $code    HTTP-Status-Code (Standard: 200)
 */
#[NoReturn] function twsJsonResponse(array $data, int $code = 200): never
{
    http_response_code($code);
    header('Content-Type: application/json; charset=utf-8');
    header('Cache-Control: no-cache, no-store, must-revalidate');
    $data['server_time'] = time();
    echo json_encode($data, JSON_UNESCAPED_UNICODE);
    exit();
}

/**
 * Setzt CORS-Header für TWS-App-Requests und verarbeitet OPTIONS-Preflight.
 */
function twsCors(): void
{
    $origin = $_SERVER['HTTP_ORIGIN'] ?? '';
    if ($origin !== '') {
        header('Access-Control-Allow-Origin: ' . $origin);
        header('Access-Control-Allow-Credentials: true');
    } else {
        header('Access-Control-Allow-Origin: *');
    }
    header('Access-Control-Allow-Methods: GET, POST, PUT, PATCH, DELETE, OPTIONS');
    header('Access-Control-Allow-Headers: Authorization, Content-Type, Accept, X-Requested-With');
    header('Access-Control-Max-Age: 86400');
    header('Vary: Origin');

    if ($_SERVER['REQUEST_METHOD'] === 'OPTIONS') {
        http_response_code(200);
        exit();
    }
}

/**
 * Liest und dekodiert den JSON-Request-Body.
 */
function twsInput(): array
{
    static $parsed = null;
    if ($parsed !== null) {
        return $parsed;
    }
    $raw    = (string)(file_get_contents('php://input') ?: '');
    $parsed = $raw !== '' ? (json_decode($raw, true) ?? []) : [];
    return $parsed;
}

/**
 * Leitet die WLS-Rolle aus dem Permissions-Array des Hauptbenutzers ab.
 */
function twsUserRole(array $permissions): string
{
    if (!empty($permissions['admin']) || !empty($permissions['wls_admin'])) {
        return 'admin';
    }
    if (!empty($permissions['wls_edit'])) {
        return 'technician';
    }
    return 'user';
}

/**
 * Wandelt eine Zeile der Haupt-users-Tabelle in das TWS-UserItem-Format um.
 *
 * @param ActiveRow $row
 */
function twsFmtUser(ActiveRow $row): array
{
    $permissions = json_decode((string)($row->offsetGet('permissions') ?? '{}'), true) ?? [];
    return [
        'id'           => (int)$row->offsetGet('id'),
        'username'     => (string)$row->offsetGet('username'),
        'name'         => trim(((string)($row->offsetGet('vname') ?? '')) . ' ' . ((string)($row->offsetGet('nname') ?? ''))),
        'email'        => (string)($row->offsetGet('email') ?? ''),
        'role'         => twsUserRole($permissions),
        'enabled'      => (bool)$row->offsetGet('enabled'),
        'indent'       => (string)$row->offsetGet('id'),
        'last_login'   => $row->offsetGet('last_login'),
        'last_logout'  => null,
        'created_at'   => $row->offsetGet('created_at'),
        'updated_at'   => null,
        'logins_total' => 0,
        'logins_failed'=> 0,
        'session_time' => 0,
    ];
}

/**
 * Wandelt eine wls_buildings-Zeile in das TWS-BuildingItem-Format um.
 */
function twsFmtBuilding($row, Database $db): array
{
    $apartmentsCount = (int)$db->getExplorer()
        ->table('mm_whg')
        ->where('haus', $row->offsetGet('building_id') ?? $row->offsetGet('id'))
        ->where('empty', 1)
        ->count('*');
    return [
        'id'               => (int)$row->offsetGet('id'),
        'name'             => (string)$row->offsetGet('name'),
        'hidden'           => !(bool)$row->offsetGet('enabled'),
        'sorted'           => (int)$row->offsetGet('sorted'),
        'created'          => $row->offsetGet('created_at'),
        'updated'          => $row->offsetGet('updated_at'),
        'apartments_count' => $apartmentsCount,
    ];
}

/**
 * Wandelt eine mm_whg-Zeile (Leerstand) in das TWS-ApartmentItem-Format um.
 */
function twsFmtApartment($row): array
{
    $label = (string)$row->offsetGet('value');
    if ($row->offsetGet('name')) {
        $label .= ' – ' . $row->offsetGet('name');
    }
    return [
        'id'          => (int)$row->offsetGet('id'),
        'building_id' => (int)$row->offsetGet('haus'),
        'number'      => $label,
        'value'       => $row->offsetGet('value'),
        'name'        => $row->offsetGet('name'),
        'sorted'      => (int)$row->offsetGet('sorted'),
        'sonder'      => (bool)$row->offsetGet('sonder'),
        'keller'      => $row->offsetGet('keller'),
        'empty'       => (bool)$row->offsetGet('empty'),
    ];
}

/**
 * Wandelt eine wls_records-Zeile in das TWS-RecordItem-Format um.
 * Berechnet duration aus start_time/end_time.
 */
function twsFmtRecord($row, Database $db): array
{
    $startTs  = $row->offsetGet('start_time') ? strtotime((string)$row->offsetGet('start_time')) : 0;
    $endTs    = $row->offsetGet('end_time')   ? strtotime((string)$row->offsetGet('end_time'))   : 0;
    $duration = max(0, $endTs - $startTs);

    $userId    = $row->offsetGet('user_id');
    $userVname = $userNname = $userEmail = '';
    if ($userId) {
        $userRow = $db->getExplorer()->table('users')->get((int)$userId);
        if ($userRow) {
            $userVname = (string)($userRow->offsetGet('vname') ?? '');
            $userNname = (string)($userRow->offsetGet('nname') ?? '');
            $userEmail = (string)($userRow->offsetGet('email') ?? '');
        }
    }

    return [
        'id'                => (int)$row->offsetGet('id'),
        'apartment_id'      => (int)$row->offsetGet('apartment_id'),
        'building_id'       => (int)$row->offsetGet('building_id'),
        'user_id'           => $userId ? (int)$userId : null,
        'start_time'        => $row->offsetGet('start_time'),
        'end_time'          => $row->offsetGet('end_time'),
        'duration'          => $duration,
        'latitude'          => $row->offsetGet('latitude')         !== null ? (float)$row->offsetGet('latitude')         : null,
        'longitude'         => $row->offsetGet('longitude')        !== null ? (float)$row->offsetGet('longitude')        : null,
        'location_accuracy' => $row->offsetGet('location_accuracy') !== null ? (float)$row->offsetGet('location_accuracy') : null,
        'created_at'        => $row->offsetGet('created_at'),
        'updated_at'        => $row->offsetGet('updated_at'),
        'user_name'         => trim($userVname . ' ' . $userNname),
        'user_email'        => $userEmail,
        'user_firstname'    => $userVname,
        'user_lastname'     => $userNname,
    ];
}

/**
 * Authentifiziert einen TWS-Request über Bearer-Token (dkc_...) oder aktive Session.
 * Gibt das formatierte TWS-Benutzerobjekt zurück oder null bei fehlender Auth.
 */
function twsGetAuth(Database $db): ?array
{
    // 1) Authorization: Bearer dkc_...
    $headers = getallheaders();
    if (isset($headers['Authorization']) && preg_match('/^Bearer\s+(dkc_\S+)$/i', $headers['Authorization'], $m)) {
        $tokenPlain = trim($m[1]);
        $utRow = $db->getExplorer()->table('user_api_tokens')
            ->where('token', hash('sha256', $tokenPlain))
            ->where('expires_at IS NULL OR expires_at > NOW()')
            ->fetch();
        if ($utRow) {
            $_SESSION['id'] = (int)$utRow->offsetGet('user_id');
            $userRow = $db->getExplorer()->table('users')
                ->where('id', $_SESSION['id'])
                ->where('enabled', 1)
                ->fetch();
            if ($userRow) {
                try {
                    $db->getExplorer()->table('user_api_tokens')
                        ->where('id', $utRow->offsetGet('id'))
                        ->update(['last_used_at' => date('Y-m-d H:i:s')]);
                } catch (\Exception $e) { /* non-fatal */ }
                return twsFmtUser($userRow);
            }
        }
        return null; // invalid/expired token
    }

    // 2) Aktive PHP-Session
    if (!empty($_SESSION['id'])) {
        $userRow = $db->getExplorer()->table('users')
            ->where('id', (int)$_SESSION['id'])
            ->where('enabled', 1)
            ->fetch();
        if ($userRow) {
            return twsFmtUser($userRow);
        }
    }

    return null;
}

/**
 * Gibt true zurück wenn der TWS-Benutzer admin oder die gefragte Permission hat.
 */
function twsHasPerm(Database $db, array $twsUser, string $perm): bool
{
    $row = $db->getExplorer()->table('users')->get($twsUser['id']);
    if (!$row) return false;
    $permissions = json_decode((string)($row->offsetGet('permissions') ?? '{}'), true) ?? [];
    return !empty($permissions['admin']) || !empty($permissions['wls_admin']) || !empty($permissions[$perm]);
}

// ============================================================================
// Haupt-Dispatcher
// ============================================================================

/**
 * Haupt-Einstiegspunkt für alle TWS-App REST-Requests.
 */
#[NoReturn] function handleTwsRequest(string $path, Database $db, Logger $log, RemoteAddress $radd): never
{
    twsCors();

    $method   = strtoupper($_SERVER['REQUEST_METHOD'] ?? 'GET');
    $segments = array_values(array_filter(explode('/', trim($path, '/'))));

    $resource = $segments[0] ?? '';

    $startTime = microtime(true);

    $log->debug('TWS request', [
        'method'   => $method,
        'path'     => $path,
        'resource' => $segments[0] ?? '',
        'ip'       => $radd->getClientIP(),
    ]);

    switch ($resource) {
        case 'health':
            twsHandleHealth($db);
            break;
        case 'user':
            twsHandleUserEndpoint($segments, $method, $db, $log, $radd);
            break;
        case 'buildings':
            twsHandleBuildingsEndpoint($segments, $method, $db, $log);
            break;
        case 'apartments':
            twsHandleApartmentsEndpoint($segments, $method, $db, $log);
            break;
        case 'records':
            twsHandleRecordsEndpoint($segments, $method, $db, $log);
            break;
        default:
            $log->warning('TWS: unknown endpoint', ['resource' => $resource, 'path' => $path]);
            twsJsonResponse(['success' => false, 'error' => 'Endpoint not found'], 404);
    }

    $log->info('TWS request completed', [
        'path'        => $path,
        'duration_ms' => round((microtime(true) - $startTime) * 1000, 2),
    ]);

    // Should never reach here; all handlers exit
    exit();
}

// ============================================================================
// Health
// ============================================================================

#[NoReturn] function twsHandleHealth(Database $db): never
{
    twsJsonResponse(['success' => true, 'status' => 'ok', 'version' => '2.0.0-integrated']);
}

// ============================================================================
// User Endpoints
// ============================================================================

#[NoReturn] function twsHandleUserEndpoint(array $segments, string $method, Database $db, Logger $log, RemoteAddress $radd): never
{
    $seg1 = $segments[1] ?? '';
    $seg2 = $segments[2] ?? '';

    // Public endpoints (no auth required)
    if ($method === 'POST' && $seg1 === 'login') {
        twsHandleUserLogin($db, $log, $radd);
    }

    if ($method === 'POST' && $seg1 === 'register') {
        twsHandleUserRegister($db, $log);
    }

    // Auth-protected endpoints
    $authUser = twsGetAuth($db);
    if (!$authUser) {
        twsJsonResponse(['success' => false, 'error' => 'Authentication required'], 401);
    }

    match(true) {
        $method === 'POST'   && $seg1 === 'logout'                              => twsHandleUserLogout($db, $log),
        in_array($method, ['GET', 'POST']) && $seg1 === 'check-token'           => twsHandleUserCheckToken($db, $authUser),
        $method === 'GET'    && $seg1 === 'get'                                 => twsHandleUserGet($seg2 !== '' && is_numeric($seg2) ? (int)$seg2 : null, $db, $authUser),
        $method === 'GET'    && $seg1 === 'list'                                => twsHandleUserList($db, $authUser),
        $method === 'POST'   && $seg1 === 'update' && is_numeric($seg2)         => twsHandleUserUpdate((int)$seg2, $db, $log, $authUser),
        $method === 'GET'    && $seg1 === 'role'                                => twsHandleUserRole($db, $authUser),
        $method === 'POST'   && $seg1 === 'setrole'                             => twsHandleUserSetRole($db, $log, $authUser),
        $method === 'DELETE' && $seg1 === 'remove' && is_numeric($seg2)         => twsHandleUserRemove((int)$seg2, $db, $log, $authUser),
        $method === 'POST'   && $seg1 === 'changepw'                            => twsHandleUserChangePw($db, $log, $authUser),
        $method === 'GET'    && $seg1 === 'photo'  && is_numeric($seg2)         => twsHandleUserPhoto((int)$seg2, $db, $authUser),
        default => twsJsonResponse(['success' => false, 'error' => 'Endpoint not found'], 404),
    };
}

/** POST /user/login */
#[NoReturn] function twsHandleUserLogin(Database $db, Logger $log, RemoteAddress $radd): never
{
    $input    = twsInput();
    $username = trim($input['username'] ?? '');
    $password = $input['password'] ?? '';

    if (!$username || !$password) {
        twsJsonResponse(['success' => false, 'error' => 'username und password sind erforderlich'], 400);
    }

    // Brute-Force-Schutz
    try {
        $bfp     = \system\helper\BruteForceProtection::getInstance($db);
        $rlCheck = $bfp->isRequestAllowed($radd->getClientIP(), 'api_login');
        if (!$rlCheck['allowed']) {
            twsJsonResponse(['success' => false, 'error' => $rlCheck['message'] ?? 'Too many requests'], 429);
        }
    } catch (\Exception $e) {
        $bfp = null;
    }

    $userRow = $db->getExplorer()->table('users')
        ->where('(username = ? OR alias = ?) AND enabled = 1', $username, $username)
        ->fetch();

    if (!$userRow || !password_verify($password, (string)$userRow->offsetGet('passwd'))) {
        if (isset($bfp)) {
            $bfp->recordLoginAttempt($username, $radd->getClientIP(), false);
        }
        usleep(random_int(150_000, 300_000));
        twsJsonResponse(['success' => false, 'error' => 'Ungültige Anmeldedaten'], 401);
    }

    if (isset($bfp)) {
        $bfp->recordLoginAttempt($username, $radd->getClientIP(), true);
        $bfp->resetFailedAttempts($username, $radd->getClientIP());
    }

    // Prüfe WLS-Zugriffsberechtigung
    $perms = json_decode((string)($userRow->offsetGet('permissions') ?? '{}'), true) ?? [];
    if (empty($perms['admin']) && empty($perms['wls_admin']) && empty($perms['wls_view']) &&
        empty($perms['wls_create']) && empty($perms['wls_edit'])) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung für die WLS-App'], 403);
    }

    // Token generieren
    try {
        $tokenPlain = 'dkc_' . bin2hex(random_bytes(32));
    } catch (\Random\RandomException $e) {
        twsJsonResponse(['success' => false, 'error' => 'Token-Generierung fehlgeschlagen'], 500);
    }

    try {
        $db->getExplorer()->table('user_api_tokens')->insert([
            'user_id'      => $userRow->offsetGet('id'),
            'token'        => hash('sha256', $tokenPlain),
            'name'         => 'TWS-App',
            'expires_at'   => (new \DateTime())->modify('+30 days')->format('Y-m-d H:i:s'),
            'last_ip'      => $radd->getClientIP(),
            'last_used_at' => date('Y-m-d H:i:s'),
            'created_at'   => date('Y-m-d H:i:s'),
        ]);
    } catch (\Exception $e) {
        $log->error('tws/user/login: token insert failed: ' . $e->getMessage());
        twsJsonResponse(['success' => false, 'error' => 'Token konnte nicht gespeichert werden'], 500);
    }

    $log->info('tws/user/login', ['user_id' => $userRow->offsetGet('id'), 'ip' => $radd->getClientIP()]);

    twsJsonResponse([
        'success' => true,
        'data'    => [
            'token' => $tokenPlain,
            'user'  => twsFmtUser($userRow),
        ],
    ]);
}

/** POST /user/register (admin-only) */
#[NoReturn] function twsHandleUserRegister(Database $db, Logger $log): never
{
    $authUser = twsGetAuth($db);
    if (!$authUser || !twsHasPerm($db, $authUser, 'admin')) {
        twsJsonResponse(['success' => false, 'error' => 'Nur Administratoren dürfen neue Benutzer registrieren'], 403);
    }

    $input    = twsInput();
    $username = trim($input['username'] ?? '');
    $password = $input['password'] ?? '';

    if (!$username || !$password) {
        twsJsonResponse(['success' => false, 'error' => 'username und password sind erforderlich'], 400);
    }

    $exists = $db->getExplorer()->table('users')->where('username', $username)->fetch();
    if ($exists) {
        twsJsonResponse(['success' => false, 'error' => 'Benutzername bereits vergeben'], 409);
    }

    $nameParts = explode(' ', trim($input['name'] ?? ''), 2);
    $row = $db->getExplorer()->table('users')->insert([
        'username'    => $username,
        'passwd'      => password_hash($password, PASSWORD_BCRYPT),
        'vname'       => $nameParts[0] ?? '',
        'nname'       => $nameParts[1] ?? '',
        'email'       => $input['email'] ?? null,
        'permissions' => json_encode(['wls_view' => true, 'wls_create' => true]),
        'enabled'     => 1,
        'created_at'  => date('Y-m-d H:i:s'),
    ]);

    twsJsonResponse(['success' => true, 'data' => twsFmtUser($row)]);
}

/** POST /user/logout */
#[NoReturn] function twsHandleUserLogout(Database $db, Logger $log): never
{
    $headers = getallheaders();
    if (isset($headers['Authorization']) && preg_match('/^Bearer\s+(dkc_\S+)$/i', $headers['Authorization'], $m)) {
        $tokenPlain = trim($m[1]);
        $db->getExplorer()->table('user_api_tokens')
            ->where('token', hash('sha256', $tokenPlain))
            ->delete();
    }
    twsJsonResponse(['success' => true, 'message' => 'Abgemeldet']);
}

/** GET /user/check-token */
#[NoReturn] function twsHandleUserCheckToken(Database $db, array $authUser): never
{
    twsJsonResponse([
        'success' => true,
        'data'    => [
            'valid'        => true,
            'session_time' => 0,
            'user_data'    => $authUser,
        ],
    ]);
}

/** GET /user/get[/{id}] */
#[NoReturn] function twsHandleUserGet(?int $id, Database $db, array $authUser): never
{
    if ($id !== null && $id !== $authUser['id']) {
        if (!twsHasPerm($db, $authUser, 'wls_admin')) {
            twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
        }
        $row = $db->getExplorer()->table('users')->where('id', $id)->where('enabled', 1)->fetch();
        if (!$row) {
            twsJsonResponse(['success' => false, 'error' => 'Benutzer nicht gefunden'], 404);
        }
        twsJsonResponse(['success' => true, 'data' => twsFmtUser($row)]);
    }

    twsJsonResponse(['success' => true, 'data' => $authUser]);
}

/** GET /user/list */
#[NoReturn] function twsHandleUserList(Database $db, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_view')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $rows  = $db->getExplorer()->table('users')->where('enabled', 1)->order('username ASC')->fetchAll();
    $items = array_map('twsFmtUser', $rows);
    twsJsonResponse(['success' => true, 'data' => $items]);
}

/** POST /user/update/{id} */
#[NoReturn] function twsHandleUserUpdate(int $id, Database $db, Logger $log, array $authUser): never
{
    if ($id !== $authUser['id'] && !twsHasPerm($db, $authUser, 'wls_admin')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('users')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Benutzer nicht gefunden'], 404);
    }

    $input = twsInput();
    $data  = [];
    if (isset($input['username'])) $data['username'] = $input['username'];
    if (isset($input['email']))    $data['email']    = $input['email'];
    if (isset($input['enabled']))  $data['enabled']  = (int)$input['enabled'];
    if (isset($input['name'])) {
        $parts = explode(' ', trim($input['name']), 2);
        $data['vname'] = $parts[0] ?? '';
        $data['nname'] = $parts[1] ?? '';
    }
    if (isset($input['role']) && twsHasPerm($db, $authUser, 'wls_admin')) {
        $perms = json_decode((string)($row->offsetGet('permissions') ?? '{}'), true) ?? [];
        unset($perms['wls_admin'], $perms['wls_edit'], $perms['wls_create'], $perms['wls_view'], $perms['wls_delete']);
        switch ($input['role']) {
            case 'admin':
                $perms = array_merge($perms, ['wls_admin' => true, 'wls_view' => true, 'wls_create' => true, 'wls_edit' => true, 'wls_delete' => true]);
                break;
            case 'technician':
                $perms = array_merge($perms, ['wls_view' => true, 'wls_create' => true, 'wls_edit' => true]);
                break;
            default:
                $perms = array_merge($perms, ['wls_view' => true, 'wls_create' => true]);
        }
        $data['permissions'] = json_encode($perms);
    }
    if (!empty($data)) {
        $row->update($data);
    }
    $log->info('tws/user/update', ['id' => $id, 'by' => $authUser['id']]);
    $row = $db->getExplorer()->table('users')->get($id);
    twsJsonResponse(['success' => true, 'data' => twsFmtUser($row)]);
}

/** GET /user/role */
#[NoReturn] function twsHandleUserRole(Database $db, array $authUser): never
{
    twsJsonResponse([
        'success' => true,
        'data'    => ['role' => $authUser['role'], 'enabled' => $authUser['enabled']],
    ]);
}

/** POST /user/setrole */
#[NoReturn] function twsHandleUserSetRole(Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_admin')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $input = twsInput();
    $id    = (int)($input['id'] ?? 0);
    $role  = $input['role'] ?? 'user';

    if (!$id) {
        twsJsonResponse(['success' => false, 'error' => 'id erforderlich'], 400);
    }

    $row = $db->getExplorer()->table('users')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Benutzer nicht gefunden'], 404);
    }

    $perms = json_decode((string)($row->offsetGet('permissions') ?? '{}'), true) ?? [];
    unset($perms['wls_admin'], $perms['wls_edit'], $perms['wls_create'], $perms['wls_view'], $perms['wls_delete']);
    switch ($role) {
        case 'admin':
            $perms = array_merge($perms, ['wls_admin' => true, 'wls_view' => true, 'wls_create' => true, 'wls_edit' => true, 'wls_delete' => true]);
            break;
        case 'technician':
            $perms = array_merge($perms, ['wls_view' => true, 'wls_create' => true, 'wls_edit' => true]);
            break;
        default:
            $perms = array_merge($perms, ['wls_view' => true, 'wls_create' => true]);
    }
    $row->update(['permissions' => json_encode($perms)]);
    $log->info('tws/user/setrole', ['id' => $id, 'role' => $role, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true]);
}

/** DELETE /user/remove/{id} */
#[NoReturn] function twsHandleUserRemove(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_admin')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('users')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Benutzer nicht gefunden'], 404);
    }

    $row->update(['enabled' => 0]);
    $log->info('tws/user/remove', ['id' => $id, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'message' => 'Benutzer deaktiviert']);
}

/** POST /user/changepw */
#[NoReturn] function twsHandleUserChangePw(Database $db, Logger $log, array $authUser): never
{
    $input       = twsInput();
    $oldPassword = $input['oldPassword'] ?? '';
    $newPassword = $input['newPassword'] ?? '';

    if (!$oldPassword || !$newPassword) {
        twsJsonResponse(['success' => false, 'error' => 'oldPassword und newPassword sind erforderlich'], 400);
    }

    $row = $db->getExplorer()->table('users')->get($authUser['id']);
    if (!$row || !password_verify($oldPassword, (string)$row->offsetGet('passwd'))) {
        twsJsonResponse(['success' => false, 'error' => 'Aktuelles Passwort ist falsch'], 401);
    }

    if (strlen($newPassword) < 8) {
        twsJsonResponse(['success' => false, 'error' => 'Neues Passwort muss mindestens 8 Zeichen haben'], 400);
    }

    $row->update(['passwd' => password_hash($newPassword, PASSWORD_BCRYPT)]);
    $log->info('tws/user/changepw', ['id' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'message' => 'Passwort geändert']);
}

/** GET /user/photo/{id} */
#[NoReturn] function twsHandleUserPhoto(int $id, Database $db, array $authUser): never
{
    // Zugriff auf eigenes Foto oder als Admin auf fremdes
    if ($id !== $authUser['id'] && !twsHasPerm($db, $authUser, 'wls_view')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    // Profilfotos werden im Haupt-System aktuell nicht unterstützt – leere Antwort
    twsJsonResponse(['success' => true, 'data' => null]);
}

// ============================================================================
// Buildings Endpoints
// ============================================================================

#[NoReturn] function twsHandleBuildingsEndpoint(array $segments, string $method, Database $db, Logger $log): never
{
    $authUser = twsGetAuth($db);
    if (!$authUser) {
        twsJsonResponse(['success' => false, 'error' => 'Authentication required'], 401);
    }
    if (!twsHasPerm($db, $authUser, 'wls_view')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $seg1 = $segments[1] ?? '';

    match(true) {
        $method === 'GET'    && $seg1 === 'list'                  => twsHandleBuildingsList($db, $authUser),
        $method === 'POST'   && $seg1 === 'create'                => twsHandleBuildingCreate($db, $log, $authUser),
        $method === 'POST'   && $seg1 === 'sync'                  => twsHandleBuildingSync($db, $log, $authUser),
        $method === 'GET'    && is_numeric($seg1)                 => twsHandleBuildingGet((int)$seg1, $db, $authUser),
        $method === 'POST'   && is_numeric($seg1)                 => twsHandleBuildingUpdate((int)$seg1, $db, $log, $authUser),
        $method === 'DELETE' && is_numeric($seg1)                 => twsHandleBuildingDelete((int)$seg1, $db, $log, $authUser),
        default => twsJsonResponse(['success' => false, 'error' => 'Endpoint not found'], 404),
    };
}

#[NoReturn] function twsHandleBuildingsList(Database $db, array $authUser): never
{
    $rows  = $db->getExplorer()->table('wls_buildings')->order('sorted ASC, name ASC')->fetchAll();
    $items = array_map(fn($r) => twsFmtBuilding($r, $db), $rows);
    twsJsonResponse(['success' => true, 'data' => $items]);
}

#[NoReturn] function twsHandleBuildingGet(int $id, Database $db, array $authUser): never
{
    $row = $db->getExplorer()->table('wls_buildings')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Gebäude nicht gefunden'], 404);
    }
    twsJsonResponse(['success' => true, 'data' => twsFmtBuilding($row, $db)]);
}

#[NoReturn] function twsHandleBuildingCreate(Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $input = twsInput();
    $name  = trim($input['name'] ?? '');
    if ($name === '') {
        twsJsonResponse(['success' => false, 'error' => 'name ist erforderlich'], 400);
    }

    $row = $db->getExplorer()->table('wls_buildings')->insert([
        'name'       => $name,
        'enabled'    => isset($input['hidden']) ? (int)!$input['hidden'] : 1,
        'sorted'     => (int)($input['sorted'] ?? 0),
        'created_at' => date('Y-m-d H:i:s'),
    ]);

    $log->info('tws/buildings/create', ['name' => $name, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'data' => twsFmtBuilding($row, $db)]);
}

#[NoReturn] function twsHandleBuildingUpdate(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('wls_buildings')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Gebäude nicht gefunden'], 404);
    }

    $input = twsInput();
    $data  = [];
    if (isset($input['name']))   $data['name']    = $input['name'];
    if (isset($input['hidden'])) $data['enabled']  = (int)!$input['hidden'];
    if (isset($input['sorted'])) $data['sorted']   = (int)$input['sorted'];
    if (!empty($data)) {
        $data['updated_at'] = date('Y-m-d H:i:s');
        $row->update($data);
    }

    $log->info('tws/buildings/update', ['id' => $id, 'by' => $authUser['id']]);
    $row = $db->getExplorer()->table('wls_buildings')->get($id);
    twsJsonResponse(['success' => true, 'data' => twsFmtBuilding($row, $db)]);
}

#[NoReturn] function twsHandleBuildingDelete(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('wls_buildings')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Gebäude nicht gefunden'], 404);
    }

    $row->delete();
    $log->info('tws/buildings/delete', ['id' => $id, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true]);
}

#[NoReturn] function twsHandleBuildingSync(Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $input     = twsInput();
    $buildings = $input['buildings'] ?? [];
    if (!is_array($buildings)) {
        twsJsonResponse(['success' => false, 'error' => 'buildings array erforderlich'], 400);
    }

    $synced = [];
    foreach ($buildings as $b) {
        $id   = isset($b['id']) ? (int)$b['id'] : 0;
        $name = trim($b['name'] ?? '');
        if ($name === '') continue;

        $data = [
            'name'       => $name,
            'enabled'    => isset($b['hidden']) ? (int)!$b['hidden'] : 1,
            'sorted'     => (int)($b['sorted'] ?? 0),
            'updated_at' => date('Y-m-d H:i:s'),
        ];

        if ($id > 0) {
            $existing = $db->getExplorer()->table('wls_buildings')->get($id);
            if ($existing) {
                $existing->update($data);
                $row = $existing;
            } else {
                $data['created_at'] = date('Y-m-d H:i:s');
                $row = $db->getExplorer()->table('wls_buildings')->insert($data);
            }
        } else {
            $data['created_at'] = date('Y-m-d H:i:s');
            $row = $db->getExplorer()->table('wls_buildings')->insert($data);
        }
        $synced[] = twsFmtBuilding($row, $db);
    }

    $log->info('tws/buildings/sync', ['count' => count($synced), 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'data' => $synced]);
}

// ============================================================================
// Apartments Endpoints
// ============================================================================

#[NoReturn] function twsHandleApartmentsEndpoint(array $segments, string $method, Database $db, Logger $log): never
{
    $authUser = twsGetAuth($db);
    if (!$authUser) {
        twsJsonResponse(['success' => false, 'error' => 'Authentication required'], 401);
    }
    if (!twsHasPerm($db, $authUser, 'wls_view')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $seg1 = $segments[1] ?? '';
    $seg2 = $segments[2] ?? '';

    match(true) {
        $method === 'GET'    && $seg1 === 'list' && $seg2 === ''             => twsHandleApartmentsList(null, $db),
        $method === 'GET'    && $seg1 === 'list' && is_numeric($seg2)        => twsHandleApartmentsList((int)$seg2, $db),
        $method === 'POST'   && $seg1 === 'create'                           => twsHandleApartmentCreate($db, $log, $authUser),
        $method === 'GET'    && is_numeric($seg1)                            => twsHandleApartmentGet((int)$seg1, $db),
        $method === 'POST'   && is_numeric($seg1)                            => twsHandleApartmentUpdate((int)$seg1, $db, $log, $authUser),
        $method === 'DELETE' && is_numeric($seg1)                            => twsHandleApartmentDelete((int)$seg1, $db, $log, $authUser),
        default => twsJsonResponse(['success' => false, 'error' => 'Endpoint not found'], 404),
    };
}

#[NoReturn] function twsHandleApartmentsList(?int $buildingId, Database $db): never
{
    $query = $db->getExplorer()
        ->table('mm_whg')
        ->where('empty', 1)
        ->order('haus ASC, sorted ASC, value ASC');
    if ($buildingId !== null) {
        $query->where('haus', $buildingId);
    }
    $rows  = $query->fetchAll();
    $items = array_map('twsFmtApartment', $rows);
    twsJsonResponse(['success' => true, 'data' => $items]);
}

#[NoReturn] function twsHandleApartmentGet(int $id, Database $db): never
{
    $row = $db->getExplorer()->table('mm_whg')->get($id);
    if (!$row || !$row->offsetGet('empty')) {
        twsJsonResponse(['success' => false, 'error' => 'Wohnung nicht gefunden'], 404);
    }
    twsJsonResponse(['success' => true, 'data' => twsFmtApartment($row)]);
}

#[NoReturn] function twsHandleApartmentCreate(Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_create')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $input = twsInput();
    if (!isset($input['building_id'], $input['value'])) {
        twsJsonResponse(['success' => false, 'error' => 'building_id und value sind erforderlich'], 400);
    }

    // Prüfe ob Gebäude in project_buildings existiert
    $buildingId = (int)$input['building_id'];
    if (!$db->getExplorer()->table('project_buildings')->where('building_id', $buildingId)->fetch()) {
        twsJsonResponse(['success' => false, 'error' => 'Gebäude nicht gefunden'], 404);
    }

    // Ermittle project_id des Gebäudes
    $building = $db->getExplorer()->table('project_buildings')->where('building_id', $buildingId)->fetch();
    $projectId = (int)$building->offsetGet('project_id');

    $row = $db->getExplorer()->table('mm_whg')->insert([
        'project_id' => $projectId,
        'haus'       => $buildingId,
        'value'      => (string)$input['value'],
        'name'       => isset($input['name']) ? (string)$input['name'] : null,
        'empty'      => 1, // Leerstand
        'sonder'     => 0,
        'keller'     => null,
        'sorted'     => (int)($input['sorted'] ?? 0),
    ]);

    $log->info('tws/apartments/create', ['value' => $input['value'], 'building_id' => $buildingId, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'data' => twsFmtApartment($row)]);
}

#[NoReturn] function twsHandleApartmentUpdate(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('mm_whg')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Wohnung nicht gefunden'], 404);
    }

    $input = twsInput();
    $data  = [];
    if (isset($input['value']))       $data['value']   = (string)$input['value'];
    if (isset($input['name']))        $data['name']    = (string)$input['name'];
    if (isset($input['sorted']))      $data['sorted']  = (int)$input['sorted'];
    if (isset($input['empty']))       $data['empty']   = (int)$input['empty'];
    if (isset($input['building_id'])) $data['haus']    = (int)$input['building_id'];

    if (!empty($data)) {
        $row->update($data);
    }

    $log->info('tws/apartments/update', ['id' => $id, 'by' => $authUser['id']]);
    $row = $db->getExplorer()->table('mm_whg')->get($id);
    twsJsonResponse(['success' => true, 'data' => twsFmtApartment($row)]);
}

#[NoReturn] function twsHandleApartmentDelete(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_delete')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('mm_whg')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Wohnung nicht gefunden'], 404);
    }

    // Soft-delete: Leerstand-Flag zurücksetzen statt endgültig löschen
    $row->update(['empty' => 0]);
    $log->info('tws/apartments/delete (set empty=0)', ['id' => $id, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true]);
}

// ============================================================================
// Records Endpoints
// ============================================================================

#[NoReturn] function twsHandleRecordsEndpoint(array $segments, string $method, Database $db, Logger $log): never
{
    $authUser = twsGetAuth($db);
    if (!$authUser) {
        twsJsonResponse(['success' => false, 'error' => 'Authentication required'], 401);
    }
    if (!twsHasPerm($db, $authUser, 'wls_view')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $seg1 = $segments[1] ?? '';
    $seg2 = $segments[2] ?? '';

    match(true) {
        $method === 'POST'   && $seg1 === 'list'                                 => twsHandleRecordsList($db, $authUser),
        $method === 'GET'    && $seg1 === 'get' && is_numeric($seg2)             => twsHandleRecordGet((int)$seg2, $db, $authUser),
        $method === 'POST'   && $seg1 === 'create'                               => twsHandleRecordCreate($db, $log, $authUser),
        $method === 'POST'   && $seg1 === 'update' && is_numeric($seg2)          => twsHandleRecordUpdate((int)$seg2, $db, $log, $authUser),
        $method === 'DELETE' && $seg1 === 'remove' && is_numeric($seg2)          => twsHandleRecordDelete((int)$seg2, $db, $log, $authUser),
        default => twsJsonResponse(['success' => false, 'error' => 'Endpoint not found'], 404),
    };
}

#[NoReturn] function twsHandleRecordsList(Database $db, array $authUser): never
{
    $input = twsInput();
    $query = $db->getExplorer()->table('wls_records');

    if (!empty($input['apartment_id']))  $query->where('apartment_id', (int)$input['apartment_id']);
    if (!empty($input['building_id']))   $query->where('building_id',  (int)$input['building_id']);
    if (!empty($input['user_id']))       $query->where('user_id',      (int)$input['user_id']);
    if (!empty($input['start_date']))    $query->where('start_time >= ?', $input['start_date']);
    if (!empty($input['end_date']))      $query->where('end_time <= ?',   $input['end_date']);

    $orderBy = in_array($input['order_by'] ?? 'start_time', ['start_time', 'end_time', 'created_at', 'id'], true)
        ? ($input['order_by'] ?? 'start_time') : 'start_time';
    $order   = strtoupper($input['order'] ?? 'DESC') === 'ASC' ? 'ASC' : 'DESC';

    $query->order($orderBy . ' ' . $order);
    if (!empty($input['limit']))  $query->limit((int)$input['limit'], (int)($input['offset'] ?? 0));

    $rows  = $query->fetchAll();
    $items = array_map(fn($r) => twsFmtRecord($r, $db), $rows);
    twsJsonResponse(['success' => true, 'data' => $items]);
}

#[NoReturn] function twsHandleRecordGet(int $id, Database $db, array $authUser): never
{
    $row = $db->getExplorer()->table('wls_records')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Datensatz nicht gefunden'], 404);
    }
    twsJsonResponse(['success' => true, 'data' => twsFmtRecord($row, $db)]);
}

#[NoReturn] function twsHandleRecordCreate(Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_create')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $input = twsInput();
    if (!isset($input['apartment_id'], $input['building_id'])) {
        twsJsonResponse(['success' => false, 'error' => 'apartment_id und building_id sind erforderlich'], 400);
    }

    $startTime = $input['start_time'] ?? date('Y-m-d H:i:s');
    $endTime   = $input['end_time']   ?? date('Y-m-d H:i:s');
    $userId    = !empty($input['user_id']) ? (int)$input['user_id'] : $authUser['id'];

    $row = $db->getExplorer()->table('wls_records')->insert([
        'apartment_id'      => (int)$input['apartment_id'],
        'building_id'       => (int)$input['building_id'],
        'user_id'           => $userId,
        'start_time'        => $startTime,
        'end_time'          => $endTime,
        'latitude'          => isset($input['latitude'])         ? (float)$input['latitude']         : null,
        'longitude'         => isset($input['longitude'])        ? (float)$input['longitude']        : null,
        'location_accuracy' => isset($input['location_accuracy']) ? (float)$input['location_accuracy'] : null,
        'created_at'        => date('Y-m-d H:i:s'),
    ]);

    // mm_whg hat keine last_flush_date-Felder – kein Update nötig

    $log->info('tws/records/create', ['apartment_id' => $input['apartment_id'], 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true, 'data' => twsFmtRecord($row, $db)]);
}

#[NoReturn] function twsHandleRecordUpdate(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_edit')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('wls_records')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Datensatz nicht gefunden'], 404);
    }

    $input = twsInput();
    $data  = [];
    if (isset($input['start_time']))        $data['start_time']        = $input['start_time'];
    if (isset($input['end_time']))          $data['end_time']          = $input['end_time'];
    if (isset($input['latitude']))          $data['latitude']          = $input['latitude'] !== null ? (float)$input['latitude'] : null;
    if (isset($input['longitude']))         $data['longitude']         = $input['longitude'] !== null ? (float)$input['longitude'] : null;
    if (isset($input['location_accuracy'])) $data['location_accuracy'] = $input['location_accuracy'] !== null ? (float)$input['location_accuracy'] : null;

    if (!empty($data)) {
        $data['updated_at'] = date('Y-m-d H:i:s');
        $row->update($data);
    }

    $log->info('tws/records/update', ['id' => $id, 'by' => $authUser['id']]);
    $row = $db->getExplorer()->table('wls_records')->get($id);
    twsJsonResponse(['success' => true, 'data' => twsFmtRecord($row, $db)]);
}

#[NoReturn] function twsHandleRecordDelete(int $id, Database $db, Logger $log, array $authUser): never
{
    if (!twsHasPerm($db, $authUser, 'wls_delete')) {
        twsJsonResponse(['success' => false, 'error' => 'Keine Berechtigung'], 403);
    }

    $row = $db->getExplorer()->table('wls_records')->get($id);
    if (!$row) {
        twsJsonResponse(['success' => false, 'error' => 'Datensatz nicht gefunden'], 404);
    }

    $row->delete();
    $log->info('tws/records/delete', ['id' => $id, 'by' => $authUser['id']]);
    twsJsonResponse(['success' => true]);
}
// ============================================================================
// Ende TWS-App REST API
// ============================================================================
