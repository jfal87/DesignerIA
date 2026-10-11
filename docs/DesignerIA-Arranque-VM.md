# Arranque de DesignerIA en una VM con VS Code

Nota de publicacion: los identificadores de infraestructura y cuentas de Windows se sustituyeron por marcadores; el documento original se conserva en el equipo de desarrollo.

Fecha: 11 de octubre de 2026.

La creacion desde API/Web y la devolucion de URL estan implementadas. La creacion local fue comprobada; el usuario tambien aplico manualmente el script de `vDesignerIATest01` en `SQL_SUR_HOST / GestionEngine`, abrio la vista en Designer y mostro su grilla en Motor SUR. Falta comprobar una creacion automatica en SUR y abrir el enlace real devuelto por DesignerIA para cerrar la fase 5 y V1. La grilla basica no incorpora el handler HTML de `vPruebaJFAL`.

## 1. Obtener el codigo actualizado

Requisitos: Windows, Git, SDK de .NET 10 y acceso a los paquetes ya declarados en los proyectos. VS Code sirve como editor; API y Web se arrancan desde dos terminales.

En una terminal de la VM:

```powershell
dotnet --list-sdks
git --version
```

Debe aparecer un SDK `10.0.xxx`. No basta con tener solamente el runtime.

Mientras el PR a main siga abierto, clonar la rama que contiene esta entrega en una carpeta nueva:

```powershell
git clone --branch feature/publish-phase5 https://github.com/jfal87/DesignerIA.git DesignerIA
```

Despues de integrar el PR, una clonacion nueva puede usar main:

```powershell
git clone https://github.com/jfal87/DesignerIA.git DesignerIA
```

Son alternativas: ejecutar solamente la que corresponda. Abrir la carpeta clonada con VS Code. No reemplazar otra copia que tenga cambios sin guardar.

Los nombres de servidor y hosts de Motor de los ejemplos publicados son marcadores. Sustituirlos por los valores privados del ambiente en el archivo local ignorado. Los datos internos reales no se publican.

## 2. Preparar la configuracion local

Desde la carpeta del repositorio, copiar el ejemplo solamente si el archivo local aun no existe:

```powershell
if (-not (Test-Path -LiteralPath '.\DesignerIA.Api\appsettings.Development.json')) {
    Copy-Item -LiteralPath '.\DesignerIA.Api\appsettings.Development.example.json' -Destination '.\DesignerIA.Api\appsettings.Development.json'
}
whoami
```

Editar el archivo copiado. Esta excluido de Git; no agregarlo a un commit.

| Ambiente | Servidor SQL / base | Base de Motor |
|---|---|---|
| Local | localhost / GestionEngine (ajustar a la instancia local real) | Vacia |
| SUR | SQL_SUR_HOST / GestionEngine | https://motor-sur.example.test |
| Norte | SQL_NORTE_HOST / GestionEngine | https://motor-norte.example.test |

Para SUR o Norte, comentar la linea activa de conexion y descomentar la del destino. Hacer lo mismo con `Motor:BaseUrl` para ese ambiente. Debe haber una sola clave activa de cada tipo. Los ejemplos mantienen la clave `GestionEngineLocal`, que consumen todos los servicios, y el alias `GestionEngine` ya definido en appsettings.json.

Configurar `ViewCreation:AllowedUser` con la identidad autenticada que usara la Web. En un arranque normal con la misma cuenta de Windows, usar el resultado de `whoami`; en JSON la barra inversa se escribe doble, por ejemplo `"TERNIUM\\tu.usuario"`. Mantener `Enabled: false`.

La conexion SQL utiliza la identidad Windows del proceso de la API. El usuario permitido para la peticion y la identidad de SQL son controles diferentes. Tener acceso desde SSMS en la VM acredita esa cuenta en esa maquina; no acredita otro proceso o equipo. No se modifica la autenticacion de la aplicacion.

Todos los servicios que usan esa clave pasan al destino elegido, incluidos salud, metadata, preflight y las herramientas de Copilot. El arranque inicial se limita a la comprobacion existente de salud; no enviar mensajes a Copilot durante la prueba de creacion.

Los ejemplos usan seguridad integrada y cifrado con validacion de certificado, sin credenciales ni `TrustServerCertificate`. La casilla de confianza de SSMS no configura la API. Si aparece un error de certificado en la API, conservar el error para resolver la confianza con el responsable del ambiente; no desactivar la validacion automaticamente.

## 3. Arrancar API y Web con creacion apagada

Comprobar que 7089 y 7276 esten libres. Los perfiles https existentes requieren un certificado de desarrollo de ASP.NET Core disponible y confiable en el navegador; cualquier ajuste de certificados de la VM se realiza por separado.

Antes del arranque, restaurar solamente las dependencias ya declaradas desde NuGet oficial, sin cambiar la configuracion global del equipo:

```powershell
dotnet restore DesignerIA.slnx --source https://api.nuget.org/v3/index.json
```

Terminal 1, desde la raiz del repositorio:

```powershell
dotnet run --project .\DesignerIA.Api\DesignerIA.Api.csproj --launch-profile https -- --ViewCreation:Enabled=false
```

Terminal 2, desde la misma raiz:

```powershell
dotnet run --project .\DesignerIA.Web\DesignerIA.Web.csproj --launch-profile https
```

Abrir https://localhost:7276. La Web usa la API en https://localhost:7089. Comprobar que API y GestionEngine figuren disponibles y que la identidad mostrada sea la prevista. Si falla, guardar el mensaje de error sin connection strings ni credenciales. No habilitar creacion hasta resolver el acceso.

## 4. Comprobacion final de fase 5

Este documento no autoriza una escritura SQL. Con autorizacion explicita para una unica vista nueva en el destino confirmado:

1. Confirmar que la API apunta al destino acordado y que la base de Motor corresponde a ese mismo ambiente.
2. Detener solamente la API propia con Ctrl+C. En su terminal, habilitar creacion para ese proceso y reiniciarla sin el argumento que la desactiva:

   ```powershell
   $env:ViewCreation__Enabled = 'true'
   dotnet run --project .\DesignerIA.Api\DesignerIA.Api.csproj --launch-profile https
   ```

3. Desde la Web, preparar una Consulta con grilla con un nombre nuevo acordado, el SP y el alias comprobados en el destino.
4. Pulsar Crear vista y confirmar una sola vez. Conservar el resultado real, IdVista, URL y diagnostico. Ante Indeterminate, comprobar el estado antes de cualquier nuevo intento.
5. Si el resultado es Created y contiene URL, pulsar Abrir vista y comprobar la grilla en Motor. Conservar captura de la URL y del render. La preview simulada no acredita esta comprobacion.
6. Al terminar, detener la API propia y quitar la variable de esa terminal:

   ```powershell
   Remove-Item Env:ViewCreation__Enabled -ErrorAction SilentlyContinue
   ```

El archivo persistente permanece con creacion apagada. Para volver al destino local, restaurar en el archivo local la pareja conexion local + base de Motor vacia y reiniciar la API. No borrar las vistas de prueba por iniciativa propia.

La URL acordada tiene el patron `{baseMotor}/GestionEngine/DefaultV2.aspx?Limpiarcache={IdVista codificado}&StyleVersion=V2`. Los parametros adicionales, los handlers y la prueba de guardar desde Designer no forman parte de esta entrega de URL.

## Evidencia y limites

El checkpoint anterior esta en `docs/DesignerIA-Fase05-Checkpoint.md` y conserva el estado previo a la entrega de URL. Para la incorporacion de URL prevalece el estado explicado al principio de esta guia. La ultima entrega de implementacion reporto 732 pruebas unitarias sin SQL superadas y compilacion con 0 errores y 17 advertencias historicas. El enlace se verifico con respuestas simuladas; queda pendiente la comprobacion completa descrita arriba.

La configuracion local, KnowledgeSource, indices generados, binarios y artifacts no se publican. La evidencia detallada disponible en el equipo de desarrollo queda en artifacts; puede faltar en la VM clonada. Los ejemplos comentados de este archivo no acreditan conectividad a SUR ni Norte.

## Pruebas en una clonacion sin corpus privado

La suite completa usa un corpus local de conocimiento que esta excluido de Git. En la copia aislada de publicacion se compilaron los proyectos con 0 errores y 4 advertencias existentes; la suite completa obtuvo 533 casos correctos y 199 fallos por los archivos o directorios privados ausentes. En el equipo original, con ese corpus, los 732 casos pasaron. Esta diferencia se conserva como limite conocido; no se publican los datos para hacer pasar las pruebas.

Las pruebas de generacion, creacion y URL son autocontenidas: los 162 casos pasaron tambien en la copia de publicacion. Para ejecutarlas:

```powershell
dotnet test .\DesignerIA.Api.Tests\DesignerIA.Api.Tests.csproj --filter "FullyQualifiedName~ViewCreationTests|FullyQualifiedName~ViewScriptGenerationTests"
```
