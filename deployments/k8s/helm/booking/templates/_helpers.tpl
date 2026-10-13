{{/* Common labels. */}}
{{- define "booking.labels" -}}
app.kubernetes.io/part-of: booking
app.kubernetes.io/managed-by: {{ .root.Release.Service }}
helm.sh/chart: {{ printf "%s-%s" .root.Chart.Name .root.Chart.Version | replace "+" "_" }}
{{ include "booking.selectorLabels" . }}
{{- end }}

{{- define "booking.selectorLabels" -}}
app.kubernetes.io/name: {{ .name }}
app.kubernetes.io/instance: {{ .root.Release.Name }}
{{- end }}

{{/* Service settings = .Values.defaults deep-merged with .Values.services.<name>. */}}
{{- define "booking.serviceValues" -}}
{{- $svc := index .root.Values.services .name -}}
{{- toYaml (mergeOverwrite (deepCopy .root.Values.defaults) (deepCopy $svc)) -}}
{{- end }}

{{- define "booking.image" -}}
{{- $g := .root.Values.global -}}
{{- $repo := printf "%s%s" $g.imageRepositoryPrefix .name -}}
{{- if $g.imageRegistry }}{{ $repo = printf "%s/%s" $g.imageRegistry $repo }}{{ end -}}
{{- printf "%s:%s" $repo (default .root.Chart.AppVersion $g.imageTag) -}}
{{- end }}

{{- define "booking.pgConnectionString" -}}
{{- printf "Server=%s;Port=%v;Database=%s;User Id=%s;Password=%s" .root.Values.postgres.host .root.Values.postgres.port .database .user .password -}}
{{- end }}

{{/* Pascal-case module name used in config keys (flight -> Flight). */}}
{{- define "booking.module" -}}
{{- title .name -}}
{{- end }}

{{/* Non-secret environment (ConfigMap). */}}
{{- define "booking.configEnv" -}}
{{- $root := .root -}}
{{- $g := $root.Values.global -}}
{{- $s := .svc -}}
{{- $module := include "booking.module" . -}}
ASPNETCORE_ENVIRONMENT: {{ $g.aspnetEnvironment | quote }}
OTEL_SERVICE_NAME: {{ $s.otelServiceName | quote }}
Jwt__Authority: {{ $g.publicUrl | quote }}
Jwt__Audience: {{ $g.jwtAudience | quote }}
{{- if $root.Values.services.identity.enabled }}
Jwt__MetadataAddress: {{ printf "http://%s/.well-known/openid-configuration" $root.Values.services.identity.serviceName | quote }}
{{- end }}
ObservabilityOptions__UseAspireOTLPExporter: "false"
ObservabilityOptions__UseJaegerExporter: "false"
ObservabilityOptions__UseZipkinExporter: "false"
{{- if $g.otlpEndpoint }}
ObservabilityOptions__UseOTLPExporter: "true"
ObservabilityOptions__OTLPOptions__OTLPGrpExporterEndpoint: {{ $g.otlpEndpoint | quote }}
OTEL_EXPORTER_OTLP_ENDPOINT: {{ $g.otlpEndpoint | quote }}
{{- else }}
ObservabilityOptions__UseOTLPExporter: "false"
{{- end }}
{{- if ne .name "gateway" }}
MessageBroker__TransportType: RabbitMq
MessageBroker__ServiceName: {{ $s.messageBrokerServiceName | quote }}
MessageBroker__RabbitMq__Host: {{ $root.Values.rabbitmq.host | quote }}
MessageBroker__RabbitMq__Port: {{ $root.Values.rabbitmq.port | quote }}
MessageBroker__RabbitMq__VirtualHost: {{ $root.Values.rabbitmq.virtualHost | quote }}
{{- end }}
{{- if $s.readDatabase }}
MongoOptions__{{ $module }}__DatabaseName: {{ $s.readDatabase | quote }}
{{- end }}
{{- if eq .name "gateway" }}
{{- range $m := list "identity" "flight" "passenger" "booking" }}
{{- $target := index $root.Values.services $m }}
ReverseProxy__Clusters__{{ $m }}__Destinations__monolith__Address: {{ printf "http://%s:80" $target.serviceName | quote }}
{{- end }}
ReverseProxy__Clusters__monolith__Destinations__monolith__Address: {{ $s.monolithAddress | quote }}
{{- range $i, $n := $s.trustedNetworks }}
TrustedProxyOptions__KnownNetworks__{{ $i }}: {{ $n | quote }}
{{- end }}
{{- end }}
{{- if eq .name "identity" }}
AuthOptions__IssuerUri: {{ $g.publicUrl | quote }}
{{- end }}
{{- if eq .name "booking" }}
Grpc__FlightAddress: {{ $s.grpcFlightAddress | quote }}
Grpc__PassengerAddress: {{ $s.grpcPassengerAddress | quote }}
Modules__Booking__BackgroundProcessingEnabled: {{ $s.backgroundProcessingEnabled | quote }}
{{- end }}
{{- range $k, $v := $s.env }}
{{ $k }}: {{ $v | quote }}
{{- end }}
{{- end }}

{{/* Secret environment (connection strings and credentials). */}}
{{- define "booking.secretEnv" -}}
{{- $root := .root -}}
{{- $s := .svc -}}
{{- $module := include "booking.module" . -}}
{{- if ne .name "gateway" }}
{{- if not $root.Values.rabbitmq.existingSecret }}
MessageBroker__RabbitMq__Username: {{ $root.Values.rabbitmq.username | quote }}
MessageBroker__RabbitMq__Password: {{ $root.Values.rabbitmq.password | quote }}
{{- end }}
{{- if $s.database.name }}
PostgresOptions__ConnectionString__{{ $module }}: {{ include "booking.pgConnectionString" (dict "root" $root "database" $s.database.name "user" $s.database.user "password" $s.database.password) | quote }}
{{- end }}
PersistMessageOptions__ConnectionString__{{ $module }}: {{ include "booking.pgConnectionString" (dict "root" $root "database" $s.persistDatabase "user" $s.database.user "password" $s.database.password) | quote }}
{{- end }}
{{- if $s.readDatabase }}
MongoOptions__ConnectionString: {{ $root.Values.mongo.connectionString | quote }}
{{- end }}
{{- if eq .name "booking" }}
EventStoreOptions__ConnectionString: {{ $root.Values.eventstore.connectionString | quote }}
{{- end }}
{{- range $k, $v := $s.secretEnv }}
{{ $k }}: {{ $v | quote }}
{{- end }}
{{- end }}

{{/* host:port for a waitFor dependency name. */}}
{{- define "booking.dependencyAddress" -}}
{{- $v := .root.Values -}}
{{- if eq .dep "postgres" }}{{ printf "%s %v" $v.postgres.host $v.postgres.port }}
{{- else if eq .dep "rabbitmq" }}{{ printf "%s %v" $v.rabbitmq.host $v.rabbitmq.port }}
{{- else if eq .dep "mongo" }}{{ (urlParse $v.mongo.connectionString).host | replace ":" " " }}
{{- else if eq .dep "eventstore" }}{{ (urlParse $v.eventstore.connectionString).host | replace ":" " " }}
{{- else }}{{ fail (printf "unknown waitFor dependency %q" .dep) }}
{{- end }}
{{- end }}
