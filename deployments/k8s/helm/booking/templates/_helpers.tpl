{{- define "booking.chart" -}}
{{- printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "booking.labels" -}}
helm.sh/chart: {{ include "booking.chart" . }}
app.kubernetes.io/name: {{ .name | default .Chart.Name }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{- define "booking.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
{{- end -}}

{{- define "booking.serviceName" -}}
{{- $service := index .root.Values.services .key -}}
{{- $default := printf "%s-service" .key -}}
{{- if eq .key "gateway" -}}
{{- $default = "gateway" -}}
{{- end -}}
{{- default $default $service.nameOverride -}}
{{- end -}}

{{- define "booking.infraName" -}}
{{- $config := index .root.Values.infrastructure .key -}}
{{- default .key $config.nameOverride -}}
{{- end -}}

{{- define "booking.postgresHost" -}}
{{- default (include "booking.infraName" (dict "root" . "key" "postgres")) .Values.postgres.host -}}
{{- end -}}

{{- define "booking.rabbitHost" -}}
{{- default (include "booking.infraName" (dict "root" . "key" "rabbitmq")) .Values.rabbitmq.host -}}
{{- end -}}

{{- define "booking.mongoHost" -}}
{{- include "booking.infraName" (dict "root" . "key" "mongo") -}}
{{- end -}}

{{- define "booking.eventstoreHost" -}}
{{- include "booking.infraName" (dict "root" . "key" "eventstore") -}}
{{- end -}}

{{- define "booking.databaseSecret" -}}
{{- $service := index .root.Values.services .key -}}
{{- default (printf "%s-database" (include "booking.serviceName" .)) $service.database.existingSecret -}}
{{- end -}}

{{- define "booking.rabbitSecret" -}}
{{- default "rabbitmq-auth" .Values.rabbitmq.auth.existingSecret -}}
{{- end -}}

{{- define "booking.mongoSecret" -}}
{{- default "mongodb-connection" .Values.mongodb.existingSecret -}}
{{- end -}}

{{- define "booking.eventstoreSecret" -}}
{{- default "eventstore-connection" .Values.eventstore.existingSecret -}}
{{- end -}}

{{- define "booking.postgresSecret" -}}
{{- default "postgres-admin" .Values.infrastructure.postgres.existingSecret -}}
{{- end -}}

{{- define "booking.validateDatabaseIdentifiers" -}}
{{- range $key, $service := .Values.services -}}
{{- if $service.database -}}
{{- range $field, $identifier := dict "username" $service.database.username "name" $service.database.name "persistMessageDatabase" $service.database.persistMessageDatabase -}}
{{- if and $identifier (not (regexMatch "^[a-z_][a-z0-9_]*$" $identifier)) -}}
{{- fail (printf "services.%s.database.%s must match ^[a-z_][a-z0-9_]*$; got %q" $key $field $identifier) -}}
{{- end -}}
{{- end -}}
{{- end -}}
{{- end -}}
{{- end -}}

{{- define "booking.validateBookingReplicas" -}}
{{- $booking := .Values.services.booking -}}
{{- $enabled := $booking.backgroundProcessingEnabled -}}
{{- $autoscaling := mergeOverwrite (deepCopy .Values.services.defaults.autoscaling) (deepCopy $booking.autoscaling) -}}
{{- $extra := default dict $booking.extraEnv -}}
{{- if hasKey $extra "Modules__Booking__BackgroundProcessingEnabled" -}}
{{- $enabled = eq (lower (toString (index $extra "Modules__Booking__BackgroundProcessingEnabled"))) "true" -}}
{{- end -}}
{{- if and $enabled (or (gt (int $booking.replicas) 1) (and $autoscaling.enabled (gt (int $autoscaling.maxReplicas) 1))) -}}
{{- fail "Booking background processing cannot use multiple replicas: the EventStoreDB $all subscription and outbox poller must remain single-active; disable background processing or keep replicas and autoscaling maxReplicas at 1." -}}
{{- end -}}
{{- end -}}

{{- define "booking.validateValues" -}}
{{- include "booking.validateDatabaseIdentifiers" . -}}
{{- include "booking.validateBookingReplicas" . -}}
{{- if not .Values.identity.signingKey.existingSecret -}}
{{- fail "identity.signingKey.existingSecret is required; create the signing-key Secret before installing the chart." -}}
{{- end -}}
{{- end -}}

{{- define "booking.commonLabels" -}}
helm.sh/chart: {{ include "booking.chart" .root }}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
{{- end -}}

{{- define "booking.persistMessageConnectionString" -}}
Server={{ include "booking.postgresHost" .root }};Port={{ .root.Values.postgres.port }};Database={{ .service.database.persistMessageDatabase }};User Id={{ .service.database.username }};Password=$(DB_PASSWORD);SSL Mode={{ .root.Values.postgres.sslMode }};Include Error Detail=true
{{- end -}}

{{- define "booking.databaseConnectionString" -}}
Server={{ include "booking.postgresHost" .root }};Port={{ .root.Values.postgres.port }};Database={{ .service.database.name }};User Id={{ .service.database.username }};Password=$(DB_PASSWORD);SSL Mode={{ .root.Values.postgres.sslMode }};Include Error Detail=true
{{- end -}}
