{{- define "booking.labels" -}}
app.kubernetes.io/name: booking
app.kubernetes.io/instance: {{ .root.Release.Name | quote }}
app.kubernetes.io/component: {{ .component | quote }}
app.kubernetes.io/part-of: booking-modular-monolith
app.kubernetes.io/managed-by: {{ .root.Release.Service | quote }}
helm.sh/chart: {{ printf "%s-%s" .root.Chart.Name .root.Chart.Version | replace "+" "_" | quote }}
{{- end -}}

{{- define "booking.image" -}}
{{- $registry := trimSuffix "/" (toString .root.Values.global.image.registry) -}}
{{- if $registry -}}
{{ printf "%s/%s:%s" $registry .service.image.repository .root.Values.global.image.tag }}
{{- else -}}
{{ printf "%s:%s" .service.image.repository .root.Values.global.image.tag }}
{{- end -}}
{{- end -}}

{{- define "booking.appEnv" -}}
{{- $root := .root -}}
{{- $service := .service -}}
{{- $key := .key -}}
{{- $name := $service.name -}}
{{- $identityName := $root.Values.services.identity.name -}}
{{- $otelServiceName := $name -}}
{{- if eq $key "gateway" -}}
{{- $otelServiceName = "booking-gateway" -}}
{{- end -}}
{{- $env := dict
  "ASPNETCORE_ENVIRONMENT" "docker"
  "Jwt__Authority" $root.Values.global.publicUrl
  "Jwt__MetadataAddress" (printf "http://%s:80/.well-known/openid-configuration" $identityName)
  "ObservabilityOptions__UseAspireOTLPExporter" "false"
  "OTEL_SERVICE_NAME" $otelServiceName
-}}
{{- if has $key (list "identity" "flight" "passenger" "booking") -}}
{{- $_ := set $env "RabbitMqOptions__HostName" $root.Values.infrastructure.rabbitmq.host -}}
{{- $_ := set $env "RabbitMqOptions__Port" (toString $root.Values.infrastructure.rabbitmq.port) -}}
{{- $_ := set $env "RabbitMqOptions__VirtualHost" (toString $root.Values.infrastructure.rabbitmq.virtualHost) -}}
{{- end -}}
{{- if eq $key "identity" -}}
{{- $_ := set $env "AuthOptions__IssuerUri" $root.Values.global.publicUrl -}}
{{- $_ := set $env "Jwt__MetadataAddress" "http://localhost:80/.well-known/openid-configuration" -}}
{{- end -}}
{{- if has $key (list "flight" "passenger" "booking") -}}
{{- $_ := set $env "Jwt__Audience" "booking-modular-monolith" -}}
{{- end -}}
{{- if has $key (list "identity" "flight" "passenger" "booking") -}}
{{- $module := title $key -}}
{{- $_ := set $env (printf "Modules__%s__BackgroundProcessingEnabled" $module) (toString $service.backgroundProcessing) -}}
{{- end -}}
{{- if has $key (list "flight" "passenger" "booking") -}}
{{- $_ := set $env (printf "MongoOptions__%s__DatabaseName" (title $key)) (toString $service.mongoDatabase) -}}
{{- end -}}
{{- if eq $key "booking" -}}
{{- $_ := set $env "Services__flight__grpc__0" (printf "http://%s:81" $root.Values.services.flight.name) -}}
{{- $_ := set $env "Services__passenger__grpc__0" (printf "http://%s:81" $root.Values.services.passenger.name) -}}
{{- end -}}
{{- if eq $key "gateway" -}}
{{- $_ := set $env "Jwt__MetadataAddress" (printf "http://%s:80/.well-known/openid-configuration" $identityName) -}}
{{- range $cluster := list "identity" "flight" "passenger" "booking" -}}
{{- $destination := index $root.Values.services $cluster -}}
{{- $_ := set $env (printf "ReverseProxy__Clusters__%s__Destinations__primary__Address" $cluster) (printf "http://%s:80/" $destination.name) -}}
{{- end -}}
{{- $_ := set $env "ReverseProxy__Clusters__monolith__Destinations__primary__Address" (printf "http://%s:80/" $identityName) -}}
{{- $_ := set $env "ReverseProxy__Routes__api-fallback__Transforms__0__PathSet" "/__gateway/unmatched" -}}
{{- $_ := set $env "ReverseProxy__Routes__monolith-fallback__Transforms__0__PathSet" "/__gateway/unmatched" -}}
{{- range $index, $origin := $root.Values.services.gateway.cors.allowedOrigins -}}
{{- $_ := set $env (printf "Cors__AllowedOrigins__%d" $index) (toString $origin) -}}
{{- end -}}
{{- end -}}
{{- if $root.Values.global.observability.otlpEndpoint -}}
{{- $_ := set $env "OTEL_EXPORTER_OTLP_ENDPOINT" $root.Values.global.observability.otlpEndpoint -}}
{{- $_ := set $env "ObservabilityOptions__OTLPOptions__OTLPGrpcExporterEndpoint" $root.Values.global.observability.otlpEndpoint -}}
{{- $_ := set $env "ObservabilityOptions__UseOTLPExporter" "true" -}}
{{- else -}}
{{- $_ := set $env "ObservabilityOptions__UseOTLPExporter" "false" -}}
{{- end -}}
{{- range $envKey, $envValue := $service.extraEnv -}}
{{- $_ := set $env $envKey (toString $envValue) -}}
{{- end -}}
{{- toYaml $env -}}
{{- end -}}

{{- define "booking.appSecret" -}}
{{- $root := .root -}}
{{- $service := .service -}}
{{- $key := .key -}}
{{- $secret := dict -}}
{{- if has $key (list "identity" "flight" "passenger" "booking") -}}
{{- $_ := set $secret "RabbitMqOptions__UserName" (toString $root.Values.infrastructure.rabbitmq.auth.username) -}}
{{- $_ := set $secret "RabbitMqOptions__Password" (toString $root.Values.infrastructure.rabbitmq.auth.password) -}}
{{- $module := title $key -}}
{{- $database := $service.database -}}
{{- $sslMode := $root.Values.infrastructure.postgres.sslMode -}}
{{- $_ := set $secret (printf "PostgresOptions__ConnectionString__%s" $module) (printf "Server=%s;Port=%v;Database=%s;User Id=%s;Password=%s;SSL Mode=%s" $root.Values.infrastructure.postgres.host $root.Values.infrastructure.postgres.port $database.name $database.user $database.password $sslMode) -}}
{{- end -}}
{{- if has $key (list "flight" "passenger" "booking") -}}
{{- $_ := set $secret "MongoOptions__ConnectionString" (printf "mongodb://%s:%v" $root.Values.infrastructure.mongo.host $root.Values.infrastructure.mongo.port) -}}
{{- end -}}
{{- if eq $key "booking" -}}
{{- $_ := set $secret "EventStoreOptions__ConnectionString" (printf "esdb://%s:%v?tls=false" $root.Values.infrastructure.eventstore.host $root.Values.infrastructure.eventstore.port) -}}
{{- end -}}
{{- toYaml $secret -}}
{{- end -}}

{{- define "booking.postgresInitSql" -}}
{{- $statements := list "\\set ON_ERROR_STOP on" -}}
{{- range $serviceKey, $service := .Values.services -}}
{{- if and $service.enabled (hasKey $service "database") -}}
{{- $database := $service.database -}}
{{- $password := replace "'" "''" (toString $database.password) -}}
{{- $statements = append $statements (printf "CREATE ROLE %s LOGIN PASSWORD '%s' NOSUPERUSER NOCREATEDB NOCREATEROLE;" $database.user $password) -}}
{{- $statements = append $statements (printf "CREATE DATABASE %s OWNER %s;" $database.name $database.user) -}}
{{- $statements = append $statements (printf "REVOKE CONNECT ON DATABASE %s FROM PUBLIC;" $database.name) -}}
{{- $statements = append $statements (printf "GRANT CONNECT ON DATABASE %s TO %s;" $database.name $database.user) -}}
{{- end -}}
{{- end -}}
{{ join "\n" $statements }}
{{- end -}}
